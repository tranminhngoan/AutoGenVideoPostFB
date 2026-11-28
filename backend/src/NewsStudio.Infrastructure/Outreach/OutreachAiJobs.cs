using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Infrastructure.Outreach;

/// <summary>
/// Runs the outreach AI writers in the background. A Gemini answer can take minutes (the first call of a process has
/// taken ~3 min), longer than the Vercel → Railway proxy keeps a request open — so the page starts a job and polls it.
/// Jobs live in memory for 30 minutes (one web process; a restart just means "try again").
/// </summary>
public class OutreachAiJobs
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(30);

    private sealed record Job(string Status, List<OutreachDraftDto>? Drafts, string? Error, DateTime StartedUtc);

    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OutreachAiJobs> _logger;

    public OutreachAiJobs(IServiceScopeFactory scopes, ILogger<OutreachAiJobs> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public Guid Start(string name, Func<OutreachService, CancellationToken, Task<List<OutreachDraftDto>>> work)
    {
        foreach (var (key, job) in _jobs)
            if (job.StartedUtc < DateTime.UtcNow - KeepFor) _jobs.TryRemove(key, out _);

        var id = Guid.NewGuid();
        _jobs[id] = new Job("running", null, null, DateTime.UtcNow);
        _ = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(Limit);
            try
            {
                using var scope = _scopes.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<OutreachService>();
                var drafts = await work(svc, cts.Token);
                _jobs[id] = new Job("done", drafts, null, _jobs[id].StartedUtc);
            }
            catch (OutreachInputException ex)
            {
                _jobs[id] = new Job("error", null, ex.Message, _jobs[id].StartedUtc);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Outreach AI job {Name} took longer than {Limit}", name, Limit);
                _jobs[id] = new Job("error", null, "AI trả lời quá chậm (hơn 4 phút). Bấm thử lại nhé.", _jobs[id].StartedUtc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outreach AI job {Name} failed", name);
                _jobs[id] = new Job("error", null, ex.Message, _jobs[id].StartedUtc);
            }
        });
        return id;
    }

    /// <summary>running | done (with drafts) | error (with message); null when unknown / expired.</summary>
    public object? Get(Guid id) =>
        _jobs.TryGetValue(id, out var j)
            ? new { status = j.Status, drafts = j.Drafts, error = j.Error, seconds = (int)(DateTime.UtcNow - j.StartedUtc).TotalSeconds }
            : null;
}
