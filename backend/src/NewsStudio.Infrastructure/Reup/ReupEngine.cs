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

namespace NewsStudio.Infrastructure.Reup;

/// <summary>Where the engine lives on the PC (set up by scripts/pc/setup-reup-engine.bat).</summary>
public class ReupPaths
{
    public string Home { get; }
    public string Python { get; }
    public string Models => Path.Combine(Home, "models");
    public string? Script { get; }

    public ReupPaths(IConfiguration configuration)
    {
        var configured = configuration["Reup:Home"];
        Home = !string.IsNullOrWhiteSpace(configured) ? configured.Trim()
            : OperatingSystem.IsWindows() && Directory.Exists(@"D:\") ? @"D:\TramTinAI"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TramTinAI");
        Python = OperatingSystem.IsWindows() ? Path.Combine(Home, "venv", "Scripts", "python.exe") : Path.Combine(Home, "venv", "bin", "python");
        Script = FFmpegRendererService.FindScriptPath("reup_engine.py");
    }

    public bool Installed => File.Exists(Python) && Script != null
                             && File.Exists(Path.Combine(Models, "face_detection_yunet_2023mar.onnx"))
                             && File.Exists(Path.Combine(Models, "rvm_mobilenetv3_fp32.onnx"));

    public bool SwapModelsPresent => File.Exists(Path.Combine(Models, "licensed", "swapper.onnx")) && File.Exists(Path.Combine(Models, "licensed", "identity.onnx"));

    public string WorkRoot(ReupSettingsDto settings) =>
        string.IsNullOrWhiteSpace(settings.WorkDir) ? Path.Combine(Home, "work") : settings.WorkDir.Trim();
}

/// <summary>Reup settings and the PC's engine report, kept as JSON rows of SystemSettings (group "Reup").</summary>
public static class ReupSettingsStore
{
    public const string SettingsKey = "Reup.Settings";
    public const string EngineKey = "Reup.Engine";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<ReupSettingsDto> GetAsync(IApplicationDbContext db, CancellationToken ct) =>
        await ReadAsync<ReupSettingsDto>(db, SettingsKey, ct) ?? new ReupSettingsDto();

    public static async Task<ReupEngineStatusDto> GetEngineAsync(IApplicationDbContext db, CancellationToken ct) =>
        await ReadAsync<ReupEngineStatusDto>(db, EngineKey, ct) ?? new ReupEngineStatusDto { Error = "Máy tính chưa báo tình trạng engine (chưa bật hoặc chưa cài)." };

    public static Task SaveAsync(IApplicationDbContext db, ReupSettingsDto s, CancellationToken ct) => WriteAsync(db, SettingsKey, s, ct);
    public static Task SaveEngineAsync(IApplicationDbContext db, ReupEngineStatusDto s, CancellationToken ct) => WriteAsync(db, EngineKey, s, ct);

    /// <summary>Keeps every value in its allowed range (the page can send anything).</summary>
    public static ReupSettingsDto Normalize(ReupSettingsDto s) => new()
    {
        Threads = Math.Clamp(s.Threads, 0, 64),
        MaxRamMb = Math.Clamp(s.MaxRamMb <= 0 ? 8192 : s.MaxRamMb, 1024, 65536),
        WorkDir = string.IsNullOrWhiteSpace(s.WorkDir) ? null : s.WorkDir.Trim(),
        ExecutionProvider = s.ExecutionProvider?.Trim().ToLowerInvariant() == "directml" ? "directml" : "cpu",
        Preset = s.Preset?.Trim().ToLowerInvariant() is "veryfast" or "faster" or "fast" or "medium" or "slow" ? s.Preset.Trim().ToLowerInvariant() : "medium",
        Crf = Math.Clamp(s.Crf <= 0 ? 18 : s.Crf, 14, 28),
        OutputRetentionDays = Math.Clamp(s.OutputRetentionDays <= 0 ? 7 : s.OutputRetentionDays, 1, 60),
        BackdropQuotaMb = Math.Clamp(s.BackdropQuotaMb <= 0 ? 3072 : s.BackdropQuotaMb, 200, 9000),
        PexelsApiKey = string.IsNullOrWhiteSpace(s.PexelsApiKey) ? null : s.PexelsApiKey.Trim(),
        PixabayApiKey = string.IsNullOrWhiteSpace(s.PixabayApiKey) ? null : s.PixabayApiKey.Trim(),
        SwapperLicenseConfirmed = s.SwapperLicenseConfirmed && !string.IsNullOrWhiteSpace(s.SwapperLicenseNote),
        SwapperLicenseNote = string.IsNullOrWhiteSpace(s.SwapperLicenseNote) ? null : s.SwapperLicenseNote.Trim()
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
        if (row == null) db.SystemSettings.Add(new SystemSetting { SettingKey = key, SettingValue = json, Group = "Reup" });
        else row.SettingValue = json;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>A line of the engine's progress output.</summary>
public sealed record ReupProgress(string Type, string? Stage, string? Message, int Done, int Total, double? Fps);

/// <summary>Runs reup_engine.py (the venv's Python) and reads its JSON lines.</summary>
public class ReupEngine
{
    private readonly ReupPaths _paths;

    public ReupEngine(ReupPaths paths) => _paths = paths;

    public ReupPaths Paths => _paths;

    private void EnsureInstalled()
    {
        if (!_paths.Installed)
            throw new InvalidOperationException($"Máy tính chưa cài engine Reup Video ({_paths.Home}) — chạy scripts\\pc\\setup-reup-engine.bat một lần.");
    }

    /// <summary>Runs a short command (probe, bench, check-face) and returns its result line; the engine's own error becomes an exception.</summary>
    public async Task<JsonObject> CommandAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        if (!File.Exists(_paths.Python) || _paths.Script == null)
            throw new InvalidOperationException($"Máy tính chưa cài engine Reup Video ({_paths.Home}) — chạy scripts\\pc\\setup-reup-engine.bat một lần.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        var all = new List<string> { "-I", _paths.Script }.Concat(args);
        var (code, stdout, stderr) = await ReupMedia.RunAsync(_paths.Python, all, limit.Token);
        JsonObject? result = null;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{')) continue;
            try { result = JsonNode.Parse(line) as JsonObject ?? result; } catch (JsonException) { /* not a result line */ }
        }
        if (result?["type"]?.GetValue<string>() == "error") throw new InvalidOperationException(result["message"]?.GetValue<string>() ?? "Engine lỗi.");
        if (result == null || code != 0) throw new InvalidOperationException("Engine không trả kết quả: " + ReupMedia.Tail(stderr + stdout));
        return result;
    }

    public Task<JsonObject> CheckFaceAsync(string image, string thumb, CancellationToken ct)
    {
        EnsureInstalled();
        return CommandAsync(new[] { "check-face", "--home", _paths.Home, "--image", image, "--thumb", thumb }, TimeSpan.FromMinutes(3), ct);
    }

    /// <summary>
    /// Processes a video. <paramref name="heartbeat"/> runs about every 2 s with the latest progress and returns true to
    /// cancel (the user pressed Huỷ); the engine (and its FFmpeg children) is killed at once. Exceeding
    /// <paramref name="maxRamMb"/> stops it with an error instead of letting the PC run out of memory.
    /// </summary>
    public async Task<JsonObject> RenderAsync(string jobJsonPath, int maxRamMb, Func<ReupProgress?, Task<bool>> heartbeat, CancellationToken ct)
    {
        EnsureInstalled();
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
        foreach (var a in new[] { "-I", _paths.Script!, "render", "--job", jobJsonPath }) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được Python của engine.");
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

        ReupProgress? latest = null;
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
                else latest = new ReupProgress(type, o["stage"]?.GetValue<string>(), o["message"]?.GetValue<string>(),
                    o["done"]?.GetValue<int>() ?? latest?.Done ?? 0, o["total"]?.GetValue<int>() ?? latest?.Total ?? 0,
                    o["fps"]?.GetValue<double>() ?? latest?.Fps);
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
                proc.Refresh();
                var ramMb = proc.WorkingSet64 / 1048576.0;
                if (ramMb > maxRamMb)
                {
                    stopReason = $"Engine dùng {ramMb:0} MB RAM, vượt giới hạn {maxRamMb} MB trong Cài đặt — đã dừng để máy không bị treo. Chọn 720p hoặc tăng giới hạn.";
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
            throw;
        }
        await proc.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(readTask, errTask);
        await heartbeat(latest);

        if (stopReason != null) throw new InvalidOperationException(stopReason);
        if (result?["type"]?.GetValue<string>() == "error") throw new InvalidOperationException(result["message"]?.GetValue<string>() ?? "Engine lỗi.");
        if (result == null || proc.ExitCode != 0)
        {
            string tail;
            lock (stderr) tail = stderr.ToString();
            throw new InvalidOperationException($"Engine dừng bất thường (mã {proc.ExitCode}): {ReupMedia.Tail(tail)}");
        }
        return result;
    }

    private static void Kill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }
}
