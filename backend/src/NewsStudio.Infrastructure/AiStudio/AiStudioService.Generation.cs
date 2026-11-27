using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>
/// The clip generator of AI Video Studio (Google Veo 3.1 Lite since 2026-11-27, Seedance before). It calls the project's existing <see cref="SeedanceVideoClient"/>
/// (same Vercel AI Gateway key and models as the video studio page, which is not touched) and writes every paid clip to
/// the same <see cref="StudioCostRecord"/> table under the same monthly budget. Money rules:
/// the user confirms the estimate first (refused when the real estimate is higher); a click is sent with a request key,
/// so a double click / resend does nothing; a scene that already has a clip being made is skipped; a job is marked
/// running before the paid call and the provider's job id is saved the moment it is accepted, so a restart, a timeout
/// or a lost download picks the same paid job up again for free ("Lấy lại clip"); a new paid attempt is always a new,
/// confirmed request; Hangfire never retries these jobs on its own.
/// </summary>
public partial class AiStudioService
{
    public AiStudioGeneratorSettingsDto GeneratorDefaults(AiStudioProject p) => new()
    {
        VideoModel = SeedanceVideoClient.NormalizeModel(p.VideoModel), Resolution = SeedanceVideoClient.NormalizeResolution(p.Resolution),
        AspectRatio = p.GenAspectRatio, Seed = p.Seed, Continuity = p.Continuity
    };

    public async Task<AiStudioProjectDto> SetGeneratorAsync(Guid id, AiStudioGeneratorSettingsDto input, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        p.VideoModel = SeedanceVideoClient.NormalizeModel(input.VideoModel); // every new clip: Veo 3.1 Lite
        p.Resolution = SeedanceVideoClient.NormalizeResolution(input.Resolution);
        p.GenAspectRatio = input.AspectRatio == "16:9" ? "16:9" : "9:16";
        p.Seed = input.Seed is { } seed ? Math.Clamp(seed, 0, int.MaxValue) : null;
        p.Continuity = input.Continuity;
        if (input.AudioMode != null)
        {
            var mode = AudioModes.FirstOrDefault(m => string.Equals(m, input.AudioMode, StringComparison.OrdinalIgnoreCase)) ?? "None";
            if (mode == "Original" && p.SourceAudioBlobId == null) throw new ArgumentException("Video tham chiếu không có âm thanh để giữ lại.");
            if (mode == "Narration" && !p.HasAudio) throw new ArgumentException("Video tham chiếu không có tiếng — không có lời bình để đọc lại.");
            p.AudioMode = mode;
        }
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }

    public async Task<AiStudioProjectDto> SetReferenceImageAsync(Guid id, string path, string contentType, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        if (new FileInfo(path).Length > MaxImageBytes) throw new ArgumentException("Ảnh quá lớn (tối đa 15 MB).");
        var head = new byte[12];
        await using (var fs = File.OpenRead(path)) _ = await fs.ReadAsync(head, ct);
        var mime = head[0] == 0xFF && head[1] == 0xD8 ? "image/jpeg"
            : head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 ? "image/png"
            : System.Text.Encoding.ASCII.GetString(head, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(head, 8, 4) == "WEBP" ? "image/webp"
            : throw new ArgumentException("Chỉ nhận ảnh JPG / PNG / WEBP.");
        var old = p.ReferenceImageBlobId;
        p.ReferenceImageBlobId = (await StoreFileAsync(path, mime, ct)).Id;
        await DeleteBlobAsync(old, ct);
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }

    public async Task<AiStudioProjectDto> RemoveReferenceImageAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        await DeleteBlobAsync(p.ReferenceImageBlobId, ct);
        p.ReferenceImageBlobId = null;
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }

    // ───────────── estimate / generate ─────────────

    private async Task<AiStudioBudgetDto> BudgetAsync(CancellationToken ct)
    {
        var budget = await _studio.MonthlyBudgetAsync(ct);
        var spent = await _studio.MonthSpentUsdAsync(ct);
        return new AiStudioBudgetDto
        {
            MonthSpentUsd = Math.Round(spent, 4), MonthlyBudgetUsd = budget,
            MonthRemainingUsd = budget <= 0 ? 0 : Math.Max(0, Math.Round(budget - spent, 4))
        };
    }

    public async Task<AiStudioEstimateDto> EstimateAsync(Guid id, List<Guid> sceneIds, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        var (lines, _) = await PlanAsync(p, sceneIds, ct);
        var dto = new AiStudioEstimateDto { Lines = lines, Budget = await BudgetAsync(ct) };
        dto.TotalUsd = Math.Round(lines.Where(l => l.Skip == null).Sum(l => l.CostUsd), 4);
        dto.TotalSeconds = lines.Where(l => l.Skip == null).Sum(l => l.Seconds);
        dto.Blocker = !_video.IsConfigured ? "Chưa có khoá Vercel AI Gateway (AI_GATEWAY_API_KEY) — Veo chưa dùng được."
            : p.Status != "Analyzed" ? "Phân tích video xong rồi mới tạo clip."
            : dto.TotalSeconds == 0 ? "Không có cảnh nào để tạo (cảnh đang tắt hoặc đang tạo)."
            : dto.Budget.MonthlyBudgetUsd > 0 && dto.Budget.MonthSpentUsd + dto.TotalUsd > dto.Budget.MonthlyBudgetUsd
                ? $"Vượt ngân sách tháng: đã chi {dto.Budget.MonthSpentUsd:0.00} USD, cần thêm ~{dto.TotalUsd:0.00} USD, trần {dto.Budget.MonthlyBudgetUsd:0.00} USD (tăng trần trong Studio Video AI › Chi phí)."
                : null;
        return dto;
    }

    /// <summary>The scenes a request would generate, in video order, with their cost and why one is skipped.</summary>
    private async Task<(List<AiStudioEstimateLineDto> Lines, List<AiStudioScene> Scenes)> PlanAsync(AiStudioProject p, List<Guid> sceneIds, CancellationToken ct)
    {
        var wanted = sceneIds.Distinct().ToHashSet();
        var scenes = await _db.AiStudioScenes.Where(s => s.ProjectId == p.Id && wanted.Contains(s.Id)).OrderBy(s => s.Order).ToListAsync(ct);
        var busy = await _db.AiStudioGenerations.Where(g => g.ProjectId == p.Id && GenRunning.Contains(g.Status)).Select(g => g.SceneId).ToListAsync(ct);
        var lines = scenes.Select(s => new AiStudioEstimateLineDto
        {
            SceneId = s.Id, Order = s.Order, Seconds = SeedanceVideoClient.ClampSeconds(s.TargetSeconds),
            CostUsd = _video.EstimateCost(p.VideoModel, p.Resolution, s.TargetSeconds),
            Skip = !s.Enabled ? "Cảnh đang tắt" : busy.Contains(s.Id) ? "Đang tạo clip cho cảnh này" : string.IsNullOrWhiteSpace(s.Prompt) ? "Prompt trống" : null
        }).ToList();
        return (lines, scenes);
    }

    public async Task<AiStudioProjectDto> GenerateAsync(Guid id, AiStudioGenerateRequestDto req, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(req.RequestKey) ? null : req.RequestKey.Trim()[..Math.Min(80, req.RequestKey.Trim().Length)];
        if (key != null && await _db.AiStudioGenerations.AnyAsync(g => g.RequestKey == key, ct))
            return await GetProjectAsync(id, ct); // the same click sent twice: already queued, nothing new is paid
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        if (!_video.IsConfigured) throw new InvalidOperationException("Chưa có khoá Vercel AI Gateway (AI_GATEWAY_API_KEY) — Veo chưa dùng được.");
        if (p.Status != "Analyzed") throw new InvalidOperationException("Phân tích video xong rồi mới tạo clip.");
        var (lines, scenes) = await PlanAsync(p, req.SceneIds, ct);
        var todo = lines.Where(l => l.Skip == null).ToList();
        if (todo.Count == 0) throw new InvalidOperationException(lines.FirstOrDefault()?.Skip ?? "Chưa chọn cảnh nào.");
        var total = Math.Round(todo.Sum(l => l.CostUsd), 4);
        if (total > req.ConfirmedCostUsd + 0.0005m)
            throw new InvalidOperationException($"Chi phí ước tính là {total:0.000} USD, cao hơn số đã xác nhận ({req.ConfirmedCostUsd:0.000} USD) — xem lại rồi xác nhận.");
        await _studio.EnsureMonthlyBudgetAsync(total, ct);

        var batch = Guid.NewGuid();
        var created = new List<AiStudioGeneration>();
        var firstEnabledOrder = await _db.AiStudioScenes.Where(s => s.ProjectId == id && s.Enabled).MinAsync(s => (int?)s.Order, ct) ?? 0;
        for (var i = 0; i < todo.Count; i++)
        {
            var s = scenes.First(x => x.Id == todo[i].SceneId);
            string? image = null;
            if (p.ReferenceImageBlobId != null && s.Order == firstEnabledOrder) image = "reference-image";
            else if (p.Continuity && s.Order > firstEnabledOrder) image = "previous-clip";
            var g = new AiStudioGeneration
            {
                ProjectId = id, SceneId = s.Id, BatchId = batch, RequestKey = i == 0 ? key : key != null ? $"{key}:{i}" : null,
                Status = "Queued", Model = SeedanceVideoClient.NormalizeModel(p.VideoModel), Resolution = SeedanceVideoClient.NormalizeResolution(p.Resolution),
                AspectRatio = p.GenAspectRatio, Seconds = todo[i].Seconds, Seed = p.Seed, Prompt = s.Prompt, ImageSource = image,
                EstimatedCostUsd = todo[i].CostUsd,
                ParamsJson = JsonSerializer.Serialize(new { promptVersion = s.PromptVersion, mode = p.PromptMode, sceneIndex = s.Index, sceneOrder = s.Order }, Json)
            };
            _db.AiStudioGenerations.Add(g);
            created.Add(g);
        }
        await _db.SaveChangesAsync(ct);
        // continuity needs the previous clip first: one job in order; otherwise one job per clip (Hangfire runs them side by side)
        if (p.Continuity) _jobs.Enqueue<AiStudioService>(s => s.RunBatchAsync(batch, CancellationToken.None));
        else foreach (var g in created) _jobs.Enqueue<AiStudioService>(s => s.RunGenerationAsync(g.Id, CancellationToken.None));
        return await GetProjectAsync(id, ct);
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunBatchAsync(Guid batchId, CancellationToken ct)
    {
        var ids = await _db.AiStudioGenerations.Where(g => g.BatchId == batchId).Join(_db.AiStudioScenes, g => g.SceneId, s => s.Id, (g, s) => new { g.Id, s.Order })
            .OrderBy(x => x.Order).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids) await RunGenerationAsync(id, ct);
    }

    /// <summary>Makes one clip (any machine: Veo works in the cloud, this only waits for it).</summary>
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunGenerationAsync(Guid generationId, CancellationToken ct)
    {
        // claim the clip in one atomic update: two workers (or a duplicated job) can never both send it to the video model
        var claimed = await _db.AiStudioGenerations.Where(x => x.Id == generationId && x.Status == "Queued")
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "Generating").SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
        if (claimed == 0) return; // already handled (duplicate job, cancelled)
        var g = await _db.AiStudioGenerations.FirstAsync(x => x.Id == generationId, ct);
        var p = await _db.AiStudioProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == g.ProjectId, ct);
        var scene = await _db.AiStudioScenes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == g.SceneId, ct);
        g.Attempts++;
        g.StartedAtUtc = DateTime.UtcNow;
        g.ErrorMessage = null;
        if (p == null)
        {
            g.Status = "Failed";
            g.ErrorMessage = "Dự án đã bị xoá.";
            await _db.SaveChangesAsync(CancellationToken.None);
            return;
        }
        try
        {
            await _studio.EnsureMonthlyBudgetAsync(g.EstimatedCostUsd, ct);
        }
        catch (InvalidOperationException ex)
        {
            g.Status = "Failed";
            g.ErrorMessage = ex.Message;
            g.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            return;
        }
        await _db.SaveChangesAsync(CancellationToken.None);

        string? temp = null;
        try
        {
            VideoStartImage? image = null;
            if (g.ImageSource != null && g.ProviderOperation == null)
            {
                temp = TempDir();
                image = await StartImageAsync(g, p, scene, temp, ct);
                if (image == null) g.ImageSource = null; // no earlier clip yet: text only (noted on the row)
            }
            var clip = await _video.GenerateAsync(g.Model, g.Prompt, g.AspectRatio, g.Resolution, g.Seconds, g.Seed, ct,
                resumeOperation: g.ProviderOperation,
                onStarted: async op =>
                {
                    g.ProviderOperation = op; // paid from here on: kept so the clip can be fetched again for free
                    await _db.SaveChangesAsync(CancellationToken.None);
                },
                image: image);
            temp ??= TempDir();
            var file = Path.Combine(temp, "clip.mp4");
            await File.WriteAllBytesAsync(file, clip.Video, ct);
            var blob = await StoreFileAsync(file, clip.MimeType, ct);
            g.ClipBlobId = blob.Id;
            g.CostUsd += clip.CostUsd;
            g.Status = "Done";
            g.FinishedAtUtc = DateTime.UtcNow;
            _db.StudioCostRecords.Add(new StudioCostRecord
            {
                Kind = "Video", Model = g.Model, Seconds = clip.Seconds, CostUsd = clip.CostUsd,
                Note = $"AI Video Studio · {Short(p.Title)} · cảnh {(scene?.Order ?? 0) + 1}, {g.Resolution}" + (clip.CostUsd == 0 ? ", lấy lại job đã trả tiền" : "")
            });
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (VideoRejectedException ex)
        {
            g.Status = "Failed";
            g.ErrorMessage = ex.Message + (g.ImageSource != null && ex.Message.Contains("image", StringComparison.OrdinalIgnoreCase)
                ? " — model này không nhận ảnh khung đầu: tắt \"Nối cảnh\" / bỏ ảnh tham chiếu rồi tạo lại." : "");
            g.FinishedAtUtc = DateTime.UtcNow;
            if (ex.BilledUsd > 0)
            {
                g.CostUsd += ex.BilledUsd;
                _db.StudioCostRecords.Add(new StudioCostRecord { Kind = "Video", Model = g.Model, Seconds = g.Seconds, CostUsd = ex.BilledUsd, Note = $"AI Video Studio · {Short(p.Title)} · bị từ chối" });
            }
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            g.Status = "Failed";
            g.ErrorMessage = g.ProviderOperation != null ? "Máy chủ khởi động lại giữa chừng — bấm \"Lấy lại clip\" (không trả tiền lần nữa)." : "Bị dừng trước khi gửi Veo — tạo lại.";
            g.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI Studio generation {Id} failed", g.Id);
            g.Status = "Failed";
            g.ErrorMessage = (ex.Message.Length > 1700 ? ex.Message[..1700] : ex.Message)
                             + (g.ProviderOperation != null && ex is not VideoAccountException ? " — job đã trả tiền: bấm \"Lấy lại clip\" (miễn phí)." : "");
            g.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            DeleteDir(temp);
        }
    }

    /// <summary>The first frame for image-to-video: the user's reference image, or the last frame of the previous scene's clip.</summary>
    private async Task<VideoStartImage?> StartImageAsync(AiStudioGeneration g, AiStudioProject p, AiStudioScene? scene, string temp, CancellationToken ct)
    {
        if (g.ImageSource == "reference-image" && p.ReferenceImageBlobId is { } refId)
        {
            var bytes = await _files.ReadAsync(refId, ct);
            var mime = await _db.StudioBlobs.AsNoTracking().Where(b => b.Id == refId).Select(b => b.MimeType).FirstOrDefaultAsync(ct) ?? "image/jpeg";
            return bytes == null ? null : new VideoStartImage(bytes, mime);
        }
        if (g.ImageSource != "previous-clip" || scene == null) return null;
        var previous = await _db.AiStudioScenes.AsNoTracking().Where(s => s.ProjectId == p.Id && s.Enabled && s.Order < scene.Order)
            .OrderByDescending(s => s.Order).Select(s => s.Id).FirstOrDefaultAsync(ct);
        if (previous == Guid.Empty) return null;
        var clipId = await _db.AiStudioGenerations.AsNoTracking().Where(x => x.SceneId == previous && x.Status == "Done" && x.ClipBlobId != null && !x.FilesDeleted)
            .OrderByDescending(x => x.FinishedAtUtc).Select(x => x.ClipBlobId).FirstOrDefaultAsync(ct);
        if (clipId is not { } cid) return null;
        var clipPath = Path.Combine(temp, "previous.mp4");
        if (!await _files.CopyToAsync(cid, clipPath, ct)) return null;
        var frame = Path.Combine(temp, "last.jpg");
        await _media.LastFrameAsync(clipPath, frame, ct);
        return new VideoStartImage(await File.ReadAllBytesAsync(frame, ct), "image/jpeg");
    }

    /// <summary>"Lấy lại clip": downloads the clip of an already-paid job again. Never submits a new job — costs nothing.</summary>
    public async Task<AiStudioGenerationDto> RecoverAsync(Guid generationId, CancellationToken ct)
    {
        var g = await _db.AiStudioGenerations.FirstOrDefaultAsync(x => x.Id == generationId, ct) ?? throw new KeyNotFoundException("Không tìm thấy clip.");
        if (g.Status != "Failed" || g.ProviderOperation == null || g.ClipBlobId != null)
            throw new InvalidOperationException("Chỉ lấy lại được clip lỗi đã gửi đi tạo (đã trả tiền). Clip khác: bấm Tạo lại.");
        g.Status = "Recovering";
        g.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<AiStudioService>(s => s.RunRecoverAsync(generationId, CancellationToken.None));
        return await GenerationDtoAsync(g, ct);
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunRecoverAsync(Guid generationId, CancellationToken ct)
    {
        var claimed = await _db.AiStudioGenerations.Where(x => x.Id == generationId && x.Status == "Recovering" && x.ProviderOperation != null)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "Generating").SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
        if (claimed == 0) return;
        var g = await _db.AiStudioGenerations.FirstAsync(x => x.Id == generationId, ct);
        var title = await _db.AiStudioProjects.AsNoTracking().Where(p => p.Id == g.ProjectId).Select(p => p.Title).FirstOrDefaultAsync(ct) ?? "";
        string? temp = null;
        try
        {
            var got = await _video.TryRecoverAsync(g.Model, g.ProviderOperation!, ct);
            if (got == null)
            {
                g.Status = "Failed";
                g.ErrorMessage = "Nhà cung cấp không còn giữ clip này (job lỗi hoặc đã hết hạn) — bấm Tạo lại (lần tạo mới sẽ tính tiền).";
            }
            else
            {
                temp = TempDir();
                var file = Path.Combine(temp, "clip.mp4");
                await File.WriteAllBytesAsync(file, got.Value.Bytes, ct);
                g.ClipBlobId = (await StoreFileAsync(file, got.Value.Mime, ct)).Id;
                g.Status = "Done";
                g.ErrorMessage = null;
                _db.StudioCostRecords.Add(new StudioCostRecord { Kind = "Video", Model = g.Model, Seconds = g.Seconds, CostUsd = 0, Note = $"AI Video Studio · {Short(title)} · lấy lại job đã trả tiền" });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            g.Status = "Failed";
            g.ErrorMessage = "Chưa lấy lại được: " + ex.Message;
        }
        finally
        {
            g.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            DeleteDir(temp);
        }
    }

    /// <summary>Only a clip not yet sent to the video model can be cancelled (a sent one is already paid and cannot be stopped there).</summary>
    public async Task<AiStudioGenerationDto> CancelGenerationAsync(Guid generationId, CancellationToken ct)
    {
        var g = await _db.AiStudioGenerations.FirstOrDefaultAsync(x => x.Id == generationId, ct) ?? throw new KeyNotFoundException("Không tìm thấy clip.");
        if (g.Status == "Generating") throw new InvalidOperationException("Clip đã gửi Veo (đã tính tiền) — không huỷ được giữa chừng, chờ clip về.");
        if (g.Status == "Queued")
        {
            g.Status = "Cancelled";
            g.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return await GenerationDtoAsync(g, ct);
    }

    public async Task<List<AiStudioGenerationDto>> GenerationsAsync(Guid? projectId, CancellationToken ct)
    {
        var q = _db.AiStudioGenerations.AsNoTracking().AsQueryable();
        if (projectId != null) q = q.Where(g => g.ProjectId == projectId);
        var rows = await q.OrderByDescending(g => g.CreatedAtUtc).Take(300).ToListAsync(ct);
        var pids = rows.Select(r => r.ProjectId).Distinct().ToList();
        var titles = await _db.AiStudioProjects.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var sids = rows.Select(r => r.SceneId).Distinct().ToList();
        var orders = await _db.AiStudioScenes.AsNoTracking().Where(s => sids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Order, ct);
        return rows.Select(g => GenDto(g, titles.GetValueOrDefault(g.ProjectId), orders)).ToList();
    }

    private async Task<AiStudioGenerationDto> GenerationDtoAsync(AiStudioGeneration g, CancellationToken ct)
    {
        var title = await _db.AiStudioProjects.AsNoTracking().Where(p => p.Id == g.ProjectId).Select(p => p.Title).FirstOrDefaultAsync(ct);
        var order = await _db.AiStudioScenes.AsNoTracking().Where(s => s.Id == g.SceneId).Select(s => (int?)s.Order).FirstOrDefaultAsync(ct);
        return GenDto(g, title, order is { } o ? new Dictionary<Guid, int> { [g.SceneId] = o } : new Dictionary<Guid, int>());
    }

    // ───────────── joined video ─────────────

    public async Task<AiStudioProjectDto> AssembleAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        if (p.FinalStatus == "Assembling") return await GetProjectAsync(id, ct);
        var (clips, missing) = await FinalClipsAsync(id, ct);
        if (missing.Count > 0) throw new InvalidOperationException($"Cảnh {string.Join(", ", missing)} chưa có clip — tạo xong (hoặc tắt cảnh đó) rồi ghép.");
        if (clips.Count == 0) throw new InvalidOperationException("Chưa có cảnh nào bật.");
        if (p.AudioMode == "Narration")
        {
            // the whole narration read in one take, with the current words, voice and style
            var scenes = await _db.AiStudioScenes.AsNoTracking().Where(s => s.ProjectId == id).ToListAsync(ct);
            var key = NarrationKeyOf(p, scenes);
            if (key != null && (p.NarrationBlobId == null || p.NarrationKey != key))
                throw new InvalidOperationException(p.NarrationBlobId == null
                    ? "Chưa đọc lời bình — bấm \"Đọc lời bình\" (tab Storyboard) trước khi ghép."
                    : "Lời bình đã sửa chữ / đổi giọng sau lần đọc trước — bấm \"Đọc lời bình\" lại trước khi ghép.");
        }
        p.FinalStatus = "Assembling";
        p.FinalError = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<AiStudioService>(s => s.RunAssembleAsync(id, CancellationToken.None));
        return await GetProjectAsync(id, ct);
    }

    private async Task<(List<AiStudioGeneration> Clips, List<int> Missing)> FinalClipsAsync(Guid id, CancellationToken ct)
    {
        var scenes = await _db.AiStudioScenes.AsNoTracking().Where(s => s.ProjectId == id && s.Enabled).OrderBy(s => s.Order).ToListAsync(ct);
        var done = await _db.AiStudioGenerations.AsNoTracking().Where(g => g.ProjectId == id && g.Status == "Done" && g.ClipBlobId != null && !g.FilesDeleted).ToListAsync(ct);
        var clips = new List<AiStudioGeneration>();
        var missing = new List<int>();
        foreach (var s in scenes)
        {
            var g = done.Where(x => x.SceneId == s.Id).OrderByDescending(x => x.FinishedAtUtc).FirstOrDefault();
            if (g == null) missing.Add(s.Order + 1); else clips.Add(g);
        }
        return (clips, missing);
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAssembleAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is not { FinalStatus: "Assembling" }) return;
        var temp = TempDir();
        try
        {
            var (clips, missing) = await FinalClipsAsync(id, ct);
            if (missing.Count > 0 || clips.Count == 0) throw new InvalidOperationException("Thiếu clip của một số cảnh.");
            var paths = new List<string>();
            for (var i = 0; i < clips.Count; i++)
            {
                var path = Path.Combine(temp, $"c{i:00}.mp4");
                if (!await _files.CopyToAsync(clips[i].ClipBlobId!.Value, path, ct)) throw new InvalidOperationException($"Clip cảnh {i + 1} đã mất khỏi kho.");
                paths.Add(path);
            }
            var same = clips.Select(c => (c.Model, c.Resolution, c.AspectRatio)).Distinct().Count() == 1;
            var (w, h) = SeedanceFrame(clips[0].AspectRatio, clips[0].Resolution);
            var output = Path.Combine(temp, "final.mp4");
            await _media.ConcatAsync(paths, output, same, w, h, ct);
            if (p.AudioMode == "Original")
            {
                // every clip gets the reference sound of its own scene (order and lengths as in the storyboard)
                var audio = Path.Combine(temp, "source-audio.m4a");
                if (p.SourceAudioBlobId is not { } aid || !await _files.CopyToAsync(aid, audio, ct))
                    throw new InvalidOperationException("Âm thanh gốc không còn trong kho (hết hạn lưu) — chọn \"Không tiếng\" rồi ghép lại.");
                var sceneIds = clips.Select(c => c.SceneId).ToList();
                var scenes = await _db.AiStudioScenes.AsNoTracking().Where(s => sceneIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
                var pieces = new List<AiStudioMedia.SoundPiece>();
                for (var i = 0; i < clips.Count; i++)
                {
                    var s = scenes[clips[i].SceneId];
                    var length = (await _media.ProbeAsync(paths[i], ct)).Duration;
                    pieces.Add(new AiStudioMedia.SoundPiece(s.StartSeconds, s.EndSeconds, length > 0 ? length : clips[i].Seconds));
                }
                var withSound = Path.Combine(temp, "final-sound.mp4");
                await _media.AddSoundAsync(output, audio, pieces, withSound, ct);
                output = withSound;
            }
            else if (p.AudioMode == "Narration")
            {
                // the one continuous reading, cut at the narrator's pauses so that every scene's words play with its own clip
                // (the clips are longer than the reference scenes: laid end to end, the reading ran ahead of the picture)
                var sceneIds = clips.Select(c => c.SceneId).ToList();
                var scenes = await _db.AiStudioScenes.AsNoTracking().Where(s => sceneIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
                string? voicePath = null;
                if (p.NarrationBlobId is { } nid && FullNarration(scenes.Values) != null)
                {
                    voicePath = Path.Combine(temp, "narration.m4a");
                    if (!await _files.CopyToAsync(nid, voicePath, ct)) throw new InvalidOperationException("Bản đọc lời bình đã mất khỏi kho — bấm \"Đọc lời bình\" lại.");
                }
                var lengths = new List<double>();
                for (var i = 0; i < clips.Count; i++)
                {
                    var length = (await _media.ProbeAsync(paths[i], ct)).Duration;
                    lengths.Add(length > 0 ? length : clips[i].Seconds);
                }
                var spoken = Enumerable.Range(0, clips.Count).Where(i => !string.IsNullOrWhiteSpace(scenes[clips[i].SceneId].Narration)).ToList();
                var parts = new Dictionary<int, (double From, double Seconds)>();
                if (voicePath != null && spoken.Count > 0)
                {
                    var take = (await _media.ProbeAsync(voicePath, ct)).Duration is > 0 and var d ? d : p.NarrationSeconds ?? 0;
                    var words = spoken.Select(i => scenes[clips[i].SceneId].Narration!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length).ToList();
                    var cuts = spoken.Count > 1 ? AiStudioMedia.ChooseCuts(take, words, await _media.SilencesAsync(voicePath, ct)) : new List<double?>();
                    double from = 0;
                    var owner = spoken[0];
                    for (var j = 0; j < spoken.Count; j++)
                    {
                        var end = j < cuts.Count ? cuts[j] : take;
                        if (end == null) continue; // no pause at this border: this scene's words go on with the next one's
                        parts[owner] = (from, end.Value - from);
                        from = end.Value;
                        if (j + 1 < spoken.Count) owner = spoken[j + 1];
                    }
                }
                var pieces = new List<AiStudioMedia.VoicePiece>();
                for (var i = 0; i < clips.Count; i++)
                    pieces.Add(parts.TryGetValue(i, out var part)
                        ? new AiStudioMedia.VoicePiece(voicePath, part.Seconds, lengths[i], part.From)
                        : new AiStudioMedia.VoicePiece(null, 0, lengths[i]));
                if (pieces.Any(x => x.VoicePath != null))
                {
                    var withVoice = Path.Combine(temp, "final-voice.mp4");
                    await _media.AddVoicesAsync(output, pieces, withVoice, ct);
                    output = withVoice;
                }
            }
            if (p.Watermark)
            {
                // the Page's name (+ brand-kit logo) moving between three corners of the Reels safe zone: hard to crop out
                var (brand, logo) = await _studio.BrandAsync(await WatermarkPageAsync(p, ct), ct);
                var png = Path.Combine(temp, "watermark.png");
                await _brandMedia.CreateWatermarkAsync(brand, logo, png, ct);
                var marked = Path.Combine(temp, "final-marked.mp4");
                await _media.AddWatermarkAsync(output, png, clips[0].AspectRatio, brand, marked, ct);
                output = marked;
            }
            var old = p.FinalBlobId;
            p.FinalBlobId = (await StoreFileAsync(output, "video/mp4", ct)).Id;
            await DeleteBlobAsync(old, ct);
            p.FinalStatus = "Done";
            p.FinalError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            p.FinalStatus = "Failed";
            p.FinalError = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message;
        }
        finally
        {
            p.FinalUpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            DeleteDir(temp);
        }
    }

    private static (int W, int H) SeedanceFrame(string aspect, string resolution)
    {
        var size = SeedanceVideoClient.FrameSize(aspect, resolution).Split('x');
        return (int.Parse(size[0]), int.Parse(size[1]));
    }

    private static string Short(string s) => s.Length > 60 ? s[..60] + "…" : s;
}
