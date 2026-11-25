using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Rendering;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>Where the analyzer lives on the PC (set up by scripts/pc/setup-ai-studio.bat).</summary>
public class AiStudioPaths
{
    public string Home { get; }
    public string Python { get; }
    public string Llm => Path.Combine(Home, "llm");
    public string? Script { get; }

    public AiStudioPaths(IConfiguration configuration)
    {
        var configured = configuration["AiStudio:Home"];
        Home = !string.IsNullOrWhiteSpace(configured) ? configured.Trim()
            : OperatingSystem.IsWindows() && Directory.Exists(@"D:\") ? @"D:\TramTinAI"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TramTinAI");
        Python = OperatingSystem.IsWindows() ? Path.Combine(Home, "venv", "Scripts", "python.exe") : Path.Combine(Home, "venv", "bin", "python");
        Script = FFmpegRendererService.FindScriptPath("studio_analyzer.py");
    }

    private string ServerExe(string backend) => Path.Combine(Llm, $"bin-{backend}", OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");

    public bool Installed => File.Exists(Python) && Script != null
                             && (File.Exists(ServerExe("vulkan")) || File.Exists(ServerExe("cpu")))
                             && Directory.Exists(Path.Combine(Llm, "models"))
                             && Directory.EnumerateFiles(Path.Combine(Llm, "models"), "Qwen3VL-*.gguf").Any();

    public string WorkRoot(AiStudioSettingsDto settings) =>
        string.IsNullOrWhiteSpace(settings.WorkDir) ? Path.Combine(Home, "work", "ai-studio") : settings.WorkDir.Trim();

    public const string NotInstalled = "Máy tính chưa cài bộ phân tích AI Video Studio — chạy scripts\\pc\\setup-ai-studio.bat một lần.";
}

/// <summary>Settings and the PC's engine report, kept as JSON rows of SystemSettings (group "AiStudio").</summary>
public static class AiStudioSettingsStore
{
    public const string SettingsKey = "AiStudio.Settings";
    public const string EngineKey = "AiStudio.Engine";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<AiStudioSettingsDto> GetAsync(IApplicationDbContext db, CancellationToken ct) =>
        Normalize(await ReadAsync<AiStudioSettingsDto>(db, SettingsKey, ct) ?? new AiStudioSettingsDto());

    public static async Task<AiStudioEngineStatusDto> GetEngineAsync(IApplicationDbContext db, CancellationToken ct) =>
        await ReadAsync<AiStudioEngineStatusDto>(db, EngineKey, ct) ?? new AiStudioEngineStatusDto { Error = "Máy tính chưa báo tình trạng bộ phân tích (chưa bật hoặc chưa cài)." };

    public static Task SaveAsync(IApplicationDbContext db, AiStudioSettingsDto s, CancellationToken ct) => WriteAsync(db, SettingsKey, Normalize(s), ct);
    public static Task SaveEngineAsync(IApplicationDbContext db, AiStudioEngineStatusDto s, CancellationToken ct) => WriteAsync(db, EngineKey, s, ct);

    public static string NormalizeModel(string? m) => m?.Trim().ToLowerInvariant() == "8b" ? "8b" : "4b";

    /// <summary>Keeps every value in its allowed range (the page can send anything).</summary>
    public static AiStudioSettingsDto Normalize(AiStudioSettingsDto s) => new()
    {
        DefaultModel = NormalizeModel(s.DefaultModel),
        Threads = Math.Clamp(s.Threads, 0, 64),
        Gpu = s.Gpu?.Trim().ToLowerInvariant() == "cpu" ? "cpu" : "auto",
        MaxFramesPerScene = Math.Clamp(s.MaxFramesPerScene <= 0 ? 8 : s.MaxFramesPerScene, 3, 10),
        Transcribe = s.Transcribe,
        WorkDir = string.IsNullOrWhiteSpace(s.WorkDir) ? null : s.WorkDir.Trim(),
        MaxRamMb = Math.Clamp(s.MaxRamMb <= 0 ? 16384 : s.MaxRamMb, 2048, 65536),
        SourceRetentionDays = Math.Clamp(s.SourceRetentionDays <= 0 ? 7 : s.SourceRetentionDays, 1, 60),
        ClipRetentionDays = Math.Clamp(s.ClipRetentionDays <= 0 ? 14 : s.ClipRetentionDays, 1, 90)
    };

    private static async Task<T?> ReadAsync<T>(IApplicationDbContext db, string key, CancellationToken ct) where T : class
    {
        var raw = await db.SystemSettings.AsNoTracking().Where(x => x.SettingKey == key).Select(x => x.SettingValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<T>(raw, Json); } catch (JsonException) { return null; }
    }

    private static async Task WriteAsync<T>(IApplicationDbContext db, string key, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, Json);
        var row = await db.SystemSettings.FirstOrDefaultAsync(x => x.SettingKey == key, ct);
        if (row == null) db.SystemSettings.Add(new SystemSetting { SettingKey = key, SettingValue = json, Group = "AiStudio" });
        else row.SettingValue = json;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>A progress line of the analyzer.</summary>
public sealed record AnalyzerProgress(string Stage, string? Message, int Done, int Total, double? Percent);

/// <summary>Runs studio_analyzer.py (the venv's Python) and reads its JSON lines.</summary>
public class AiStudioEngine
{
    private readonly AiStudioPaths _paths;

    public AiStudioEngine(AiStudioPaths paths) => _paths = paths;

    public AiStudioPaths Paths => _paths;

    /// <summary>A short command (probe, bench) → its result line; the engine's own error becomes an exception.</summary>
    public async Task<JsonObject> CommandAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        if (!File.Exists(_paths.Python) || _paths.Script == null) throw new InvalidOperationException(AiStudioPaths.NotInstalled);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        var (code, stdout, stderr) = await AiStudioMedia.RunAsync(_paths.Python, new List<string> { "-I", _paths.Script }.Concat(args), limit.Token);
        JsonObject? result = null;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{')) continue;
            try { result = JsonNode.Parse(line) as JsonObject ?? result; } catch (JsonException) { /* not a result line */ }
        }
        if (result?["type"]?.GetValue<string>() == "error") throw new InvalidOperationException(result["message"]?.GetValue<string>() ?? "Bộ phân tích lỗi.");
        if (result == null || code != 0) throw new InvalidOperationException("Bộ phân tích không trả kết quả: " + AiStudioMedia.Tail(stderr + stdout));
        return result;
    }

    /// <summary>
    /// Analyses a video. <paramref name="heartbeat"/> runs about every 2 s with the latest progress and returns true to
    /// cancel: the analyzer and its children (FFmpeg, llama-server) are killed at once. Going over
    /// <paramref name="maxRamMb"/> stops it with an error instead of letting the PC run out of memory.
    /// </summary>
    public async Task<JsonObject> AnalyzeAsync(string jobJsonPath, int maxRamMb, Func<AnalyzerProgress?, Task<bool>> heartbeat, CancellationToken ct)
    {
        if (!_paths.Installed) throw new InvalidOperationException(AiStudioPaths.NotInstalled);
        KillStrayServers(); // a model server left by an interrupted run would hold the GPU and RAM
        var psi = new ProcessStartInfo
        {
            FileName = _paths.Python,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in new[] { "-I", _paths.Script!, "analyze", "--job", jobJsonPath }) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được Python của bộ phân tích.");
        var stderr = new StringBuilder();
        var errTask = Task.Run(async () =>
        {
            while (await proc.StandardError.ReadLineAsync() is { } l)
            {
                lock (stderr)
                {
                    stderr.AppendLine(l);
                    if (stderr.Length > 8000) stderr.Remove(0, stderr.Length - 8000);
                }
            }
        }, CancellationToken.None);

        AnalyzerProgress? latest = null;
        JsonObject? result = null;
        var readTask = Task.Run(async () =>
        {
            while (await proc.StandardOutput.ReadLineAsync() is { } line)
            {
                if (!line.StartsWith('{')) continue;
                JsonObject? o;
                try { o = JsonNode.Parse(line) as JsonObject; } catch (JsonException) { continue; }
                if (o == null) continue;
                var type = o["type"]?.GetValue<string>() ?? "";
                if (type is "done" or "error") result = o;
                else latest = new AnalyzerProgress(o["stage"]?.GetValue<string>() ?? "", o["message"]?.GetValue<string>(),
                    o["done"]?.GetValue<int>() ?? 0, o["total"]?.GetValue<int>() ?? 0, o["percent"]?.GetValue<double>());
            }
        }, CancellationToken.None);

        string? stopReason = null;
        try
        {
            while (!proc.HasExited)
            {
                await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(TimeSpan.FromSeconds(2), ct));
                ct.ThrowIfCancellationRequested();
                if (proc.HasExited) break;
                var ramMb = TreeWorkingSetMb(proc);
                if (ramMb > maxRamMb)
                {
                    stopReason = $"Bộ phân tích dùng {ramMb:0} MB RAM, vượt giới hạn {maxRamMb} MB trong Cài đặt — đã dừng để máy không bị treo. Chọn model 4B hoặc tăng giới hạn.";
                    Kill(proc);
                    break;
                }
                if (await heartbeat(latest))
                {
                    Kill(proc);
                    throw new OperationCanceledException("Người dùng đã huỷ.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Kill(proc);
            KillStrayServers();
            throw;
        }
        await proc.WaitForExitAsync(CancellationToken.None);
        KillStrayServers();
        await Task.WhenAll(readTask, errTask);
        await heartbeat(latest);

        if (stopReason != null) throw new InvalidOperationException(stopReason);
        if (result?["type"]?.GetValue<string>() == "error") throw new InvalidOperationException(result["message"]?.GetValue<string>() ?? "Bộ phân tích lỗi.");
        if (result == null || proc.ExitCode != 0)
        {
            string tail;
            lock (stderr) tail = stderr.ToString();
            throw new InvalidOperationException($"Bộ phân tích dừng bất thường (mã {proc.ExitCode}): {AiStudioMedia.Tail(tail)}");
        }
        return result;
    }

    /// <summary>RAM of the analyzer plus the model server it started (the model is the big part).</summary>
    private static double TreeWorkingSetMb(Process root)
    {
        double total = 0;
        try
        {
            root.Refresh();
            total += root.WorkingSet64;
            foreach (var p in Process.GetProcessesByName("llama-server"))
            {
                try { total += p.WorkingSet64; } catch { /* exited */ } finally { p.Dispose(); }
            }
        }
        catch { /* exited */ }
        return total / 1048576.0;
    }

    private static void Kill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }

    /// <summary>
    /// Stops model servers of THIS engine (llama-server under the AI Studio folder) still running when no analysis is:
    /// a killed analyzer can leave its server behind (seen: 9 GB of RAM held after an aborted run). Only one analysis
    /// runs at a time, so a server found here belongs to no one. Other programs' llama-server are never touched.
    /// </summary>
    public void KillStrayServers()
    {
        var root = Path.GetFullPath(_paths.Llm).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var p in Process.GetProcessesByName("llama-server"))
        {
            try
            {
                var file = p.MainModule?.FileName;
                if (file != null && Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
            }
            catch { /* exited, or not ours to read */ }
            finally { p.Dispose(); }
        }
    }
}
