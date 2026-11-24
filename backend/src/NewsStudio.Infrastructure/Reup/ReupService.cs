using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.Reup;

/// <summary>
/// "Reup Video" — free video processing on the owner's PC, no paid API, no cloud GPU:
/// AI characters (face checked on the PC), a backdrop library with source + licence of every file, and jobs that
/// swap the face (only with a commercially licensed model) and/or replace the background, set the audio and render an
/// H.264 MP4 that is checked before it is called done. Heavy work runs only in the Hangfire queue <see cref="PcQueue"/>,
/// which only the PC listens to: with the PC off, jobs wait instead of costing Railway CPU.
/// </summary>
public class ReupService
{
    public const string PcQueue = "pc_local";
    public const long MaxSourceBytes = 600L * 1024 * 1024;
    public const double MaxSourceSeconds = 300;
    public const double PreviewSeconds = 5;
    public static readonly string[] AudioModes = { "Remove", "KeepOriginal", "Replace", "Mix" };
    public static readonly string[] Categories = { "Beach", "Forest", "Flowers", "Street", "Cafe", "Apartment", "Pool", "Nature", "Dance", "Other" };
    private static readonly string[] Running = { "Queued", "Processing" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    /// <summary>One video at a time on the PC: two at once would only split the CPU and double the RAM.</summary>
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    private readonly IApplicationDbContext _db;
    private readonly StudioFileStore _files;
    private readonly ReupMedia _media;
    private readonly ReupEngine _engine;
    private readonly ReupStockClient _stock;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<ReupService> _logger;

    public ReupService(IApplicationDbContext db, StudioFileStore files, ReupMedia media, ReupEngine engine, ReupStockClient stock,
        IBackgroundJobClient jobs, ILogger<ReupService> logger)
    {
        _db = db;
        _files = files;
        _media = media;
        _engine = engine;
        _stock = stock;
        _jobs = jobs;
        _logger = logger;
    }

    // ═══════════════════════════════ files ═══════════════════════════════

    private async Task<StudioBlob> StoreFileAsync(string path, string mime, CancellationToken ct)
    {
        var blob = new StudioBlob { MimeType = mime, Data = Array.Empty<byte>(), SizeBytes = new FileInfo(path).Length };
        await _files.SaveFileAsync(blob.Id, path, mime, keepLocal: false, ct);
        _db.StudioBlobs.Add(blob);
        return blob;
    }

    private async Task DeleteBlobAsync(Guid? id, CancellationToken ct)
    {
        if (id is not { } blobId) return;
        _files.Delete(blobId);
        var row = await _db.StudioBlobs.FirstOrDefaultAsync(b => b.Id == blobId, ct);
        if (row != null) _db.StudioBlobs.Remove(row);
    }

    /// <summary>A stored file on this machine's disk (fetched from the bucket when needed) for streaming to the page.</summary>
    public async Task<(string Path, string MimeType)?> FileAsync(Guid? blobId, CancellationToken ct)
    {
        if (blobId is not { } id) return null;
        var mime = await _db.StudioBlobs.AsNoTracking().Where(b => b.Id == id).Select(b => b.MimeType).FirstOrDefaultAsync(ct);
        if (mime == null) return null;
        var path = await _files.LocalPathAsync(id, ct);
        return path == null ? null : (path, mime);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "reup-upload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteDir(string? dir)
    {
        if (dir == null) return;
        for (var i = 0; i < 3; i++)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
            catch (IOException) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) { Thread.Sleep(500); }
        }
    }

    // ═══════════════════════════════ characters ═══════════════════════════════

    public async Task<List<ReupCharacterDto>> ListCharactersAsync(CancellationToken ct)
    {
        var used = await _db.ReupJobs.AsNoTracking().Where(j => j.CharacterId != null)
            .GroupBy(j => j.CharacterId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var rows = await _db.ReupCharacters.AsNoTracking().OrderByDescending(c => c.CreatedAtUtc).ToListAsync(ct);
        return rows.Select(c => ToDto(c, used.GetValueOrDefault(c.Id))).ToList();
    }

    private static ReupCharacterDto ToDto(ReupCharacter c, int used) => new()
    {
        Id = c.Id, Name = c.Name, Notes = c.Notes, ConsentConfirmed = c.ConsentConfirmed, Status = c.Status, RejectReason = c.RejectReason,
        FaceScore = c.FaceScore, HasThumb = c.ThumbBlobId != null, UsedByJobs = used, CreatedAtUtc = c.CreatedAtUtc, CheckedAtUtc = c.CheckedAtUtc
    };

    public async Task<ReupCharacterDto> AddCharacterAsync(string name, string? notes, bool consent, string imagePath, string mime, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Nhập tên nhân vật.");
        if (!consent) throw new ArgumentException("Cần xác nhận: ảnh do AI tạo, hoặc người trong ảnh đã đồng ý cho dùng khuôn mặt.");
        if (mime is not ("image/jpeg" or "image/png" or "image/webp")) throw new ArgumentException("Ảnh nhân vật phải là JPG, PNG hoặc WEBP.");
        var blob = await StoreFileAsync(imagePath, mime, ct);
        var c = new ReupCharacter { Name = name.Trim(), Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), ConsentConfirmed = true, ImageBlobId = blob.Id };
        _db.ReupCharacters.Add(c);
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<ReupService>(s => s.CheckCharacterAsync(c.Id, CancellationToken.None));
        return ToDto(c, 0);
    }

    public async Task<ReupCharacterDto> UpdateCharacterAsync(Guid id, ReupCharacterUpdateDto input, CancellationToken ct)
    {
        var c = await _db.ReupCharacters.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy nhân vật.");
        if (input.Name != null)
        {
            if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Tên nhân vật không được trống.");
            c.Name = input.Name.Trim();
        }
        if (input.Notes != null) c.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        await _db.SaveChangesAsync(ct);
        return ToDto(c, await _db.ReupJobs.CountAsync(j => j.CharacterId == id, ct));
    }

    public async Task<ReupCharacterDto> RecheckCharacterAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.ReupCharacters.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy nhân vật.");
        c.Status = "Checking";
        c.RejectReason = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<ReupService>(s => s.CheckCharacterAsync(c.Id, CancellationToken.None));
        return ToDto(c, await _db.ReupJobs.CountAsync(j => j.CharacterId == id, ct));
    }

    public async Task DeleteCharacterAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.ReupCharacters.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy nhân vật.");
        if (await _db.ReupJobs.AnyAsync(j => j.CharacterId == id && Running.Contains(j.Status), ct))
            throw new InvalidOperationException("Nhân vật đang được dùng trong một video đang xử lý — huỷ video đó trước.");
        foreach (var j in await _db.ReupJobs.Where(j => j.CharacterId == id).ToListAsync(ct)) j.CharacterId = null;
        await DeleteBlobAsync(c.ImageBlobId, ct);
        await DeleteBlobAsync(c.ThumbBlobId, ct);
        _db.ReupCharacters.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> CharacterBlobAsync(Guid id, bool thumb, CancellationToken ct) =>
        await _db.ReupCharacters.AsNoTracking().Where(c => c.Id == id).Select(c => thumb ? c.ThumbBlobId : (Guid?)c.ImageBlobId).FirstOrDefaultAsync(ct);

    /// <summary>On the PC: exactly one clear, large, frontal face? Stores the face crop as thumbnail.</summary>
    [Queue(PcQueue)]
    [AutomaticRetry(Attempts = 1)]
    public async Task CheckCharacterAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.ReupCharacters.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return;
        var dir = TempDir();
        try
        {
            var img = Path.Combine(dir, "character.img");
            if (!await _files.CopyToAsync(c.ImageBlobId, img, ct)) throw new InvalidOperationException("Ảnh nhân vật đã mất khỏi kho file — tải ảnh lên lại.");
            var thumb = Path.Combine(dir, "thumb.jpg");
            var r = await _engine.CheckFaceAsync(img, thumb, ct);
            var ok = r["ok"]?.GetValue<bool>() == true;
            c.Status = ok ? "Ready" : "Rejected";
            c.RejectReason = ok ? null : r["reason"]?.GetValue<string>() ?? "Ảnh không đạt.";
            c.FaceScore = r["score"]?.GetValue<double>();
            c.FaceBoxJson = r["box"]?.ToJsonString();
            c.EmbeddingJson = ok ? r["embedding"]?.ToJsonString() : null;
            c.CheckedAtUtc = DateTime.UtcNow;
            if (File.Exists(thumb))
            {
                await DeleteBlobAsync(c.ThumbBlobId, ct);
                c.ThumbBlobId = (await StoreFileAsync(thumb, "image/jpeg", ct)).Id;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            c.Status = "Error";
            c.RejectReason = ex.Message;
        }
        finally
        {
            DeleteDir(dir);
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ═══════════════════════════════ backdrops ═══════════════════════════════

    public async Task<List<ReupBackdropDto>> ListBackdropsAsync(string? category, string? kind, string? q, CancellationToken ct)
    {
        var query = _db.ReupBackdrops.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(b => b.Category == category);
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(b => b.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = "%" + q.Trim().Replace("%", "").Replace("_", "") + "%";
            query = query.Where(b => EF.Functions.ILike(b.Name, like) || (b.Tags != null && EF.Functions.ILike(b.Tags, like)) || (b.Author != null && EF.Functions.ILike(b.Author, like)));
        }
        return (await query.OrderByDescending(b => b.CreatedAtUtc).ToListAsync(ct)).Select(ToDto).ToList();
    }

    private static ReupBackdropDto ToDto(ReupBackdrop b) => new()
    {
        Id = b.Id, Name = b.Name, Kind = b.Kind, Category = b.Category, Tags = b.Tags, MimeType = b.MimeType, SizeBytes = b.SizeBytes,
        Width = b.Width, Height = b.Height, DurationSeconds = b.DurationSeconds, SourceName = b.SourceName, SourceUrl = b.SourceUrl,
        Author = b.Author, LicenseName = b.LicenseName, LicenseUrl = b.LicenseUrl, DownloadedAtUtc = b.DownloadedAtUtc,
        HasThumb = b.ThumbBlobId != null, UseCount = b.UseCount
    };

    public static string NormalizeCategory(string? c) =>
        Categories.FirstOrDefault(x => string.Equals(x, c?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Other";

    /// <summary>Adds a picture / video already on this server's disk to the library, with its source and licence (both required).</summary>
    public async Task<ReupBackdropDto> AddBackdropAsync(string path, string? fileName, ReupBackdropInfoDto info, CancellationToken ct)
    {
        var source = string.IsNullOrWhiteSpace(info.SourceName) ? "Upload" : info.SourceName.Trim();
        var license = info.LicenseName?.Trim();
        if (string.IsNullOrWhiteSpace(license))
        {
            if (source != "Upload") throw new ArgumentException("Ghi rõ giấy phép của file (ví dụ: Pexels License, Pixabay Content License, CC0…).");
            license = "Của tôi (tự quay / tự chụp)";
        }
        var p = await _media.ProbeAsync(path, ct);
        if (!p.HasVideo || p.Width <= 0) throw new ArgumentException("File không phải ảnh hoặc video hợp lệ.");
        var isVideo = p.Duration >= 0.5 && (p.Frames is null or > 1) && p.VideoCodec is not ("mjpeg" or "png" or "webp" or "bmp" or "gif");
        if (Math.Min(p.Width, p.Height) < 360) throw new ArgumentException($"Cảnh quá nhỏ ({p.Width}×{p.Height}) — cần ít nhất 360 px cạnh ngắn, nên từ 1080 px.");
        if (isVideo && p.Duration < 1) throw new ArgumentException("Video nền quá ngắn (cần ít nhất 1 giây).");

        var settings = ReupSettingsStore.Normalize(await ReupSettingsStore.GetAsync(_db, ct));
        var size = new FileInfo(path).Length;
        var used = await _db.ReupBackdrops.SumAsync(b => (long?)b.SizeBytes, ct) ?? 0;
        if ((used + size) / 1048576.0 > settings.BackdropQuotaMb)
            throw new InvalidOperationException($"Thư viện cảnh đã dùng {used / 1048576.0:0} MB / {settings.BackdropQuotaMb} MB — xoá bớt cảnh hoặc tăng hạn mức trong Cài đặt (kho miễn phí 10 GB).");

        var mime = isVideo ? "video/mp4" : p.VideoCodec switch { "png" => "image/png", "webp" => "image/webp", _ => "image/jpeg" };
        var thumbPath = path + ".thumb.jpg";
        Guid? thumbId = null;
        try
        {
            await _media.ThumbnailAsync(path, thumbPath, !isVideo, isVideo ? Math.Min(1, p.Duration / 2) : 0, ct);
            thumbId = (await StoreFileAsync(thumbPath, "image/jpeg", ct)).Id;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Reup backdrop thumbnail failed: {Message}", ex.Message);
        }
        finally
        {
            if (File.Exists(thumbPath)) File.Delete(thumbPath);
        }
        var blob = await StoreFileAsync(path, mime, ct);
        var name = !string.IsNullOrWhiteSpace(info.Name) ? info.Name.Trim()
            : !string.IsNullOrWhiteSpace(fileName) ? Path.GetFileNameWithoutExtension(fileName) : $"Cảnh {DateTime.Now:dd/MM HH:mm}";
        var b = new ReupBackdrop
        {
            Name = name.Length > 200 ? name[..200] : name, Kind = isVideo ? "Video" : "Image", Category = NormalizeCategory(info.Category),
            Tags = string.IsNullOrWhiteSpace(info.Tags) ? null : info.Tags.Trim(), BlobId = blob.Id, ThumbBlobId = thumbId, MimeType = mime,
            SizeBytes = size, Width = p.Width, Height = p.Height, DurationSeconds = isVideo ? Math.Round(p.Duration, 2) : null,
            SourceName = source, SourceUrl = string.IsNullOrWhiteSpace(info.SourceUrl) ? null : info.SourceUrl.Trim(),
            Author = string.IsNullOrWhiteSpace(info.Author) ? null : info.Author.Trim(), LicenseName = license,
            LicenseUrl = string.IsNullOrWhiteSpace(info.LicenseUrl) ? null : info.LicenseUrl.Trim(), DownloadedAtUtc = DateTime.UtcNow
        };
        _db.ReupBackdrops.Add(b);
        await _db.SaveChangesAsync(ct);
        return ToDto(b);
    }

    public async Task<ReupBackdropDto> ImportBackdropAsync(ReupBackdropImportDto input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.SourceName) || input.SourceName.Trim() == "Upload") input.SourceName = "Other";
        if (string.IsNullOrWhiteSpace(input.SourceUrl)) input.SourceUrl = input.Url;
        if (string.IsNullOrWhiteSpace(input.LicenseName)) throw new ArgumentException("Ghi rõ giấy phép của file (ví dụ: Pexels License, CC0…).");
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "download.bin");
            await _stock.DownloadAsync(input.Url, path, ct);
            return await AddBackdropAsync(path, null, input, ct);
        }
        finally
        {
            DeleteDir(dir);
        }
    }

    public async Task<List<ReupStockItemDto>> SearchStockAsync(string provider, string kind, string query, CancellationToken ct)
    {
        var s = await ReupSettingsStore.GetAsync(_db, ct);
        return await _stock.SearchAsync(provider, kind, query, s.PexelsApiKey, s.PixabayApiKey, ct);
    }

    public async Task<ReupBackdropDto> ImportStockAsync(ReupStockImportDto input, CancellationToken ct)
    {
        var item = input.Item;
        if (!ReupStockClient.IsProviderHost(item.Provider, item.DownloadUrl))
            throw new ArgumentException("Link tải không thuộc Pexels / Pixabay.");
        var pexels = item.Provider == "Pexels";
        var info = new ReupBackdropImportDto
        {
            Url = item.DownloadUrl, Name = input.Name ?? $"{item.Provider} {item.Id}", Category = input.Category, Tags = input.Tags,
            SourceName = item.Provider, SourceUrl = item.PageUrl, Author = item.Author,
            LicenseName = pexels ? ReupStockClient.PexelsLicense : ReupStockClient.PixabayLicense,
            LicenseUrl = pexels ? ReupStockClient.PexelsLicenseUrl : ReupStockClient.PixabayLicenseUrl
        };
        return await ImportBackdropAsync(info, ct);
    }

    public async Task<ReupBackdropDto> UpdateBackdropAsync(Guid id, ReupBackdropInfoDto input, CancellationToken ct)
    {
        var b = await _db.ReupBackdrops.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy cảnh.");
        if (!string.IsNullOrWhiteSpace(input.Name)) b.Name = input.Name.Trim();
        if (input.Category != null) b.Category = NormalizeCategory(input.Category);
        if (input.Tags != null) b.Tags = string.IsNullOrWhiteSpace(input.Tags) ? null : input.Tags.Trim();
        if (input.Author != null) b.Author = string.IsNullOrWhiteSpace(input.Author) ? null : input.Author.Trim();
        if (input.SourceUrl != null) b.SourceUrl = string.IsNullOrWhiteSpace(input.SourceUrl) ? null : input.SourceUrl.Trim();
        if (!string.IsNullOrWhiteSpace(input.LicenseName)) b.LicenseName = input.LicenseName.Trim();
        if (input.LicenseUrl != null) b.LicenseUrl = string.IsNullOrWhiteSpace(input.LicenseUrl) ? null : input.LicenseUrl.Trim();
        await _db.SaveChangesAsync(ct);
        return ToDto(b);
    }

    public async Task DeleteBackdropAsync(Guid id, CancellationToken ct)
    {
        var b = await _db.ReupBackdrops.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy cảnh.");
        var key = id.ToString();
        var busy = await _db.ReupJobs.AsNoTracking().Where(j => Running.Contains(j.Status)).Select(j => j.SegmentsJson).ToListAsync(ct);
        if (busy.Any(s => s.Contains(key, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Cảnh đang được dùng trong một video đang xử lý — huỷ video đó trước.");
        await DeleteBlobAsync(b.BlobId, ct);
        await DeleteBlobAsync(b.ThumbBlobId, ct);
        _db.ReupBackdrops.Remove(b);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> BackdropBlobAsync(Guid id, bool thumb, CancellationToken ct) =>
        await _db.ReupBackdrops.AsNoTracking().Where(b => b.Id == id).Select(b => thumb ? b.ThumbBlobId : (Guid?)b.BlobId).FirstOrDefaultAsync(ct);

    // ═══════════════════════════════ jobs ═══════════════════════════════

    public static List<ReupSegmentDto> ReadSegments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<ReupSegmentDto>>(json, Json) ?? new(); } catch (JsonException) { return new(); }
    }

    public static ReupPlacementDto ReadPlacement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<ReupPlacementDto>(json, Json) ?? new(); } catch (JsonException) { return new(); }
    }

    public static ReupPlacementDto NormalizePlacement(ReupPlacementDto? p)
    {
        p ??= new ReupPlacementDto();
        return new ReupPlacementDto
        {
            Scale = Math.Clamp(p.Scale <= 0 ? 1 : p.Scale, 0.5, 1.3),
            OffsetX = Math.Clamp(p.OffsetX, -0.4, 0.4),
            OffsetY = Math.Clamp(p.OffsetY, -0.3, 0.3),
            Harmonize = Math.Clamp(p.Harmonize, 0, 1),
            LightWrap = Math.Clamp(p.LightWrap, 0, 1),
            Blur = Math.Clamp(p.Blur, 0, 10),
            Crossfade = Math.Clamp(p.Crossfade, 0, 2),
            KenBurns = p.KenBurns
        };
    }

    /// <summary>Scenes in time order, the first from 0 s, none past the end of the video, no two at the same time.</summary>
    public static List<ReupSegmentDto> NormalizeSegments(IEnumerable<ReupSegmentDto>? segments, double duration)
    {
        var list = (segments ?? Enumerable.Empty<ReupSegmentDto>()).Where(s => s.BackdropId != Guid.Empty)
            .Select(s => new ReupSegmentDto { Start = Math.Round(Math.Max(0, s.Start), 2), BackdropId = s.BackdropId })
            .Where(s => s.Start < Math.Max(0.5, duration - 0.25))
            .OrderBy(s => s.Start).ToList();
        var result = new List<ReupSegmentDto>();
        foreach (var s in list)
        {
            if (result.Count > 0 && s.Start - result[^1].Start < 0.5) result[^1] = s; // same moment: the later choice wins
            else result.Add(s);
        }
        if (result.Count > 0) result[0].Start = 0;
        return result;
    }

    private static ReupJobDto ToDto(ReupJob j, string? characterName = null)
    {
        var (w, h) = j.SourceWidth > 0 ? ReupMedia.OutputSize(j.SourceWidth, j.SourceHeight, j.OutputShortSide) : (0, 0);
        List<ReupQcCheckDto> qc;
        try { qc = string.IsNullOrWhiteSpace(j.QcReportJson) ? new() : JsonSerializer.Deserialize<List<ReupQcCheckDto>>(j.QcReportJson, Json) ?? new(); }
        catch (JsonException) { qc = new(); }
        Dictionary<string, double>? stats = null;
        try { if (!string.IsNullOrWhiteSpace(j.StatsJson)) stats = JsonSerializer.Deserialize<Dictionary<string, double>>(j.StatsJson, Json); }
        catch (JsonException) { /* old / partial */ }
        return new ReupJobDto
        {
            Id = j.Id, Title = j.Title, RightsConfirmed = j.RightsConfirmed, PeopleConsentConfirmed = j.PeopleConsentConfirmed, RightsNote = j.RightsNote,
            FaceSwapEnabled = j.FaceSwapEnabled, CharacterId = j.CharacterId, CharacterName = characterName, BackgroundEnabled = j.BackgroundEnabled,
            Segments = ReadSegments(j.SegmentsJson), Placement = ReadPlacement(j.PlacementJson), AudioMode = j.AudioMode,
            AudioVolume = j.AudioVolume, OriginalVolume = j.OriginalVolume, OutputShortSide = j.OutputShortSide,
            SourceFileName = j.SourceFileName, SourceSizeBytes = j.SourceSizeBytes, SourceDurationSeconds = j.SourceDurationSeconds,
            SourceFps = j.SourceFps, SourceFpsValue = Math.Round(ReupMedia.FpsValue(j.SourceFps), 3), SourceWidth = j.SourceWidth, SourceHeight = j.SourceHeight,
            SourceHasAudio = j.SourceHasAudio, HasSourceThumb = j.SourceThumbBlobId != null, AudioFileName = j.AudioFileName,
            OutputWidth = j.OutputWidth ?? w, OutputHeight = j.OutputHeight ?? h, Status = j.Status, PreviewRun = j.PreviewRun, Stage = j.Stage,
            ProgressText = j.ProgressText, ProgressPercent = j.ProgressPercent, FramesDone = j.FramesDone, FramesTotal = j.FramesTotal, Fps = j.Fps,
            CancelRequested = j.CancelRequested, WorkerMachine = j.WorkerMachine, CreatedAtUtc = j.CreatedAtUtc, StartedAtUtc = j.StartedAtUtc,
            FinishedAtUtc = j.FinishedAtUtc, ErrorMessage = j.ErrorMessage, HasPreview = j.PreviewBlobId != null, HasOutput = j.OutputBlobId != null,
            OutputDurationSeconds = j.OutputDurationSeconds, OutputSizeBytes = j.OutputSizeBytes, Qc = qc, Stats = stats,
            ExpiresAtUtc = j.ExpiresAtUtc, FilesDeleted = j.FilesDeleted
        };
    }

    public async Task<List<ReupJobDto>> ListJobsAsync(CancellationToken ct)
    {
        var names = await _db.ReupCharacters.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var rows = await _db.ReupJobs.AsNoTracking().OrderByDescending(j => j.CreatedAtUtc).Take(200).ToListAsync(ct);
        return rows.Select(j => ToDto(j, j.CharacterId is { } c ? names.GetValueOrDefault(c) : null)).ToList();
    }

    public async Task<ReupJobDto> GetJobAsync(Guid id, CancellationToken ct)
    {
        var j = await _db.ReupJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        var name = j.CharacterId is { } c ? await _db.ReupCharacters.Where(x => x.Id == c).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return ToDto(j, name);
    }

    /// <summary>A new job from an uploaded source video (already on this server's disk).</summary>
    public async Task<ReupJobDto> CreateJobAsync(string path, string fileName, string? title, CancellationToken ct)
    {
        var size = new FileInfo(path).Length;
        if (size > MaxSourceBytes) throw new ArgumentException("Video quá lớn (tối đa 600 MB).");
        var p = await _media.ProbeAsync(path, ct);
        if (!p.HasVideo || p.Duration < 1) throw new ArgumentException("File không phải video hợp lệ (hoặc ngắn hơn 1 giây).");
        if (p.Duration > MaxSourceSeconds) throw new ArgumentException($"Video dài {p.Duration:0}s — tối đa {MaxSourceSeconds / 60:0} phút cho một lần xử lý.");
        if (p.Width < 240 || p.Height < 240) throw new ArgumentException($"Video quá nhỏ ({p.Width}×{p.Height}).");

        var thumbPath = path + ".thumb.jpg";
        Guid? thumbId = null;
        try
        {
            await _media.ThumbnailAsync(path, thumbPath, false, Math.Min(1, p.Duration / 2), ct);
            thumbId = (await StoreFileAsync(thumbPath, "image/jpeg", ct)).Id;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Reup source thumbnail failed: {Message}", ex.Message);
        }
        finally
        {
            if (File.Exists(thumbPath)) File.Delete(thumbPath);
        }
        var blob = await StoreFileAsync(path, "video/mp4", ct);
        var shortSide = Math.Min(p.Width, p.Height);
        var j = new ReupJob
        {
            Title = !string.IsNullOrWhiteSpace(title) ? title.Trim() : Path.GetFileNameWithoutExtension(fileName),
            SourceBlobId = blob.Id, SourceThumbBlobId = thumbId, SourceFileName = Path.GetFileName(fileName), SourceSizeBytes = size,
            SourceDurationSeconds = Math.Round(p.Duration, 3), SourceFps = p.FpsText, SourceWidth = p.Width, SourceHeight = p.Height,
            SourceHasAudio = p.HasAudio, OutputShortSide = shortSide >= 1080 ? 1080 : 720,
            PlacementJson = JsonSerializer.Serialize(new ReupPlacementDto(), Json)
        };
        if (j.Title.Length > 300) j.Title = j.Title[..300];
        _db.ReupJobs.Add(j);
        await _db.SaveChangesAsync(ct);
        return ToDto(j);
    }

    private async Task<ReupJob> EditableJobAsync(Guid id, CancellationToken ct)
    {
        var j = await _db.ReupJobs.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        if (Running.Contains(j.Status)) throw new InvalidOperationException("Video đang xử lý — huỷ trước khi sửa.");
        return j;
    }

    public async Task<ReupJobDto> UpdateJobAsync(Guid id, ReupJobInputDto input, CancellationToken ct)
    {
        var j = await EditableJobAsync(id, ct);
        if (!string.IsNullOrWhiteSpace(input.Title)) j.Title = input.Title.Trim().Length > 300 ? input.Title.Trim()[..300] : input.Title.Trim();
        j.RightsConfirmed = input.RightsConfirmed;
        j.PeopleConsentConfirmed = input.PeopleConsentConfirmed;
        j.RightsNote = string.IsNullOrWhiteSpace(input.RightsNote) ? null : input.RightsNote.Trim();
        j.FaceSwapEnabled = input.FaceSwapEnabled;
        if (input.CharacterId is { } cid && !await _db.ReupCharacters.AnyAsync(c => c.Id == cid, ct)) throw new ArgumentException("Nhân vật không còn tồn tại.");
        j.CharacterId = input.CharacterId;
        j.BackgroundEnabled = input.BackgroundEnabled;
        var segments = NormalizeSegments(input.Segments, j.SourceDurationSeconds);
        var ids = segments.Select(s => s.BackdropId).Distinct().ToList();
        if (ids.Count > 0 && await _db.ReupBackdrops.CountAsync(b => ids.Contains(b.Id), ct) != ids.Count) throw new ArgumentException("Có cảnh nền không còn tồn tại.");
        j.SegmentsJson = JsonSerializer.Serialize(segments, Json);
        j.PlacementJson = JsonSerializer.Serialize(NormalizePlacement(input.Placement), Json);
        if (!AudioModes.Contains(input.AudioMode)) throw new ArgumentException("Chế độ âm thanh không hợp lệ.");
        j.AudioMode = input.AudioMode;
        j.AudioVolume = Math.Clamp(input.AudioVolume, 0, 2);
        j.OriginalVolume = Math.Clamp(input.OriginalVolume, 0, 2);
        j.OutputShortSide = input.OutputShortSide >= 1080 ? 1080 : 720;
        await _db.SaveChangesAsync(ct);
        return await GetJobAsync(id, ct);
    }

    public async Task<ReupJobDto> SetAudioAsync(Guid id, string path, string fileName, CancellationToken ct)
    {
        var j = await EditableJobAsync(id, ct);
        var p = await _media.ProbeAsync(path, ct);
        if (!p.HasAudio) throw new ArgumentException("File không có âm thanh.");
        await DeleteBlobAsync(j.AudioBlobId, ct);
        j.AudioBlobId = (await StoreFileAsync(path, "audio/mpeg", ct)).Id;
        j.AudioFileName = Path.GetFileName(fileName);
        if (j.AudioMode is "Remove" or "KeepOriginal") j.AudioMode = "Replace";
        await _db.SaveChangesAsync(ct);
        return await GetJobAsync(id, ct);
    }

    public async Task<ReupJobDto> RemoveAudioAsync(Guid id, CancellationToken ct)
    {
        var j = await EditableJobAsync(id, ct);
        await DeleteBlobAsync(j.AudioBlobId, ct);
        j.AudioBlobId = null;
        j.AudioFileName = null;
        if (j.AudioMode is "Replace" or "Mix") j.AudioMode = "Remove";
        await _db.SaveChangesAsync(ct);
        return await GetJobAsync(id, ct);
    }

    /// <summary>Why a job can't run as set up (null = ready). Checked when starting and again on the PC.</summary>
    public async Task<string?> BlockerAsync(ReupJob j, CancellationToken ct)
    {
        if (j.FilesDeleted) return "File nguồn đã hết hạn lưu và bị xoá — tải video lên lại.";
        if (!j.RightsConfirmed) return "Cần xác nhận bạn có quyền sử dụng và chỉnh sửa video nguồn.";
        if (!j.PeopleConsentConfirmed) return "Cần xác nhận người xuất hiện trong video đã đồng ý cho chỉnh sửa và đăng lại.";
        if (!j.FaceSwapEnabled && !j.BackgroundEnabled)
            return "Chưa bật Thay cảnh nền hay Thay khuôn mặt — chạy như vậy chỉ xuất lại video (đổi tiếng). Bật ít nhất một mục rồi chạy.";
        if (j.FaceSwapEnabled)
        {
            if (await FaceSwapBlockerAsync(ct) is { } swap) return swap;
            if (j.CharacterId == null) return "Chọn nhân vật để thay mặt.";
            var status = await _db.ReupCharacters.Where(c => c.Id == j.CharacterId).Select(c => c.Status).FirstOrDefaultAsync(ct);
            if (status == null) return "Nhân vật đã bị xoá.";
            if (status != "Ready") return "Ảnh nhân vật chưa qua kiểm tra khuôn mặt.";
        }
        if (j.BackgroundEnabled)
        {
            var segments = ReadSegments(j.SegmentsJson);
            if (segments.Count == 0) return "Chọn ít nhất một cảnh nền.";
            var ids = segments.Select(s => s.BackdropId).Distinct().ToList();
            if (await _db.ReupBackdrops.CountAsync(b => ids.Contains(b.Id), ct) != ids.Count) return "Có cảnh nền đã bị xoá — chọn lại.";
        }
        if (j.AudioMode is "KeepOriginal" or "Mix" && !j.SourceHasAudio) return "Video nguồn không có âm thanh để giữ lại.";
        if (j.AudioMode is "Replace" or "Mix" && j.AudioBlobId == null) return "Chọn file âm thanh.";
        return null;
    }

    /// <summary>Face swap needs a model the owner holds a commercial licence for (never downloaded by the tool).</summary>
    public async Task<string?> FaceSwapBlockerAsync(CancellationToken ct)
    {
        var s = ReupSettingsStore.Normalize(await ReupSettingsStore.GetAsync(_db, ct));
        if (!s.SwapperLicenseConfirmed)
            return "Thay mặt đang khoá: các model thay mặt phổ biến chỉ cho dùng phi thương mại. Khi có model kèm GIẤY PHÉP THƯƠNG MẠI, đặt vào thư mục models\\licensed và xác nhận trong Cài đặt.";
        var engine = await ReupSettingsStore.GetEngineAsync(_db, ct);
        if (engine.CheckedAtUtc != null && !(engine.Models.GetValueOrDefault("swapper") && engine.Models.GetValueOrDefault("identity")))
            return "Máy tính chưa có file models\\licensed\\swapper.onnx và identity.onnx.";
        return null;
    }

    public async Task<ReupJobDto> StartAsync(Guid id, bool preview, CancellationToken ct)
    {
        var j = await EditableJobAsync(id, ct);
        if (await BlockerAsync(j, ct) is { } why) throw new InvalidOperationException(why);
        j.Status = "Queued";
        j.PreviewRun = preview;
        j.CancelRequested = false;
        j.ErrorMessage = null;
        j.Stage = "queued";
        j.ProgressPercent = 0;
        j.FramesDone = 0;
        j.Fps = null;
        j.ProgressText = "Chờ máy tính nhận việc…";
        await _db.SaveChangesAsync(ct);
        j.HangfireJobId = _jobs.Enqueue<ReupService>(s => s.RunJobAsync(id, preview, CancellationToken.None));
        await _db.SaveChangesAsync(ct);
        return await GetJobAsync(id, ct);
    }

    public async Task<ReupJobDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var j = await _db.ReupJobs.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        if (!Running.Contains(j.Status)) return await GetJobAsync(id, ct);
        j.CancelRequested = true;
        if (j.Status == "Queued")
        {
            j.Status = AfterCancel(j);
            j.ProgressText = j.Status == "Done" ? "Đã huỷ lần chạy lại — giữ video kết quả trước." : "Đã huỷ.";
            j.FinishedAtUtc = DateTime.UtcNow;
        }
        else j.ProgressText = "Đang huỷ…";
        await _db.SaveChangesAsync(ct);
        if (j.HangfireJobId != null)
        {
            try { _jobs.Delete(j.HangfireJobId); } catch (Exception ex) { _logger.LogInformation("Reup cancel: {Message}", ex.Message); }
        }
        return await GetJobAsync(id, ct);
    }

    public async Task DeleteJobAsync(Guid id, CancellationToken ct)
    {
        var j = await _db.ReupJobs.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        if (Running.Contains(j.Status)) throw new InvalidOperationException("Video đang xử lý — huỷ trước khi xoá.");
        foreach (var b in new[] { j.SourceBlobId, j.SourceThumbBlobId, j.AudioBlobId, j.PreviewBlobId, j.OutputBlobId }) await DeleteBlobAsync(b, ct);
        _db.ReupJobs.Remove(j);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> JobBlobAsync(Guid id, string which, CancellationToken ct)
    {
        var j = await _db.ReupJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j == null) return null;
        return which switch
        {
            "source" => j.FilesDeleted ? null : j.SourceBlobId,
            "thumb" => j.SourceThumbBlobId,
            "preview" => j.PreviewBlobId,
            "output" => j.OutputBlobId,
            "audio" => j.AudioBlobId,
            _ => null
        };
    }

    // ═══════════════════════════════ the PC runner ═══════════════════════════════

    /// <summary>Processes a video on the PC (queue <see cref="PcQueue"/>). preview = only the first 5 seconds, to check the look.</summary>
    [Queue(PcQueue)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunJobAsync(Guid id, bool preview, CancellationToken ct)
    {
        if (!await RunLock.WaitAsync(0, ct))
        {
            await NoteAsync(id, "Chờ video trước xử lý xong (máy tính làm lần lượt từng video)…", ct);
            await RunLock.WaitAsync(ct);
        }
        try
        {
            await RunCoreAsync(id, preview, ct);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task NoteAsync(Guid id, string text, CancellationToken ct)
    {
        var j = await _db.ReupJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j == null || j.Status != "Queued") return;
        j.ProgressText = text;
        await _db.SaveChangesAsync(ct);
    }

    private async Task RunCoreAsync(Guid id, bool preview, CancellationToken ct)
    {
        var j = await _db.ReupJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j == null) return;
        if (j.Status != "Queued" || j.CancelRequested)
        {
            if (j.Status == "Queued") { j.Status = "Cancelled"; j.ProgressText = "Đã huỷ."; await _db.SaveChangesAsync(CancellationToken.None); }
            return;
        }
        var settings = ReupSettingsStore.Normalize(await ReupSettingsStore.GetAsync(_db, ct));
        j.Status = "Processing";
        j.Stage = "preparing";
        j.ProgressText = "Đang chuẩn bị file trên máy tính…";
        j.ProgressPercent = 1;
        j.StartedAtUtc = DateTime.UtcNow;
        j.FinishedAtUtc = null;
        j.WorkerMachine = Environment.MachineName;
        await _db.SaveChangesAsync(ct);

        string? work = null;
        try
        {
            if (await BlockerAsync(j, ct) is { } why) throw new InvalidOperationException(why);
            if (!_engine.Paths.Installed)
                throw new InvalidOperationException($"Máy tính chưa cài engine Reup Video ({_engine.Paths.Home}) — chạy scripts\\pc\\setup-reup-engine.bat một lần.");
            var root = _engine.Paths.WorkRoot(settings);
            Directory.CreateDirectory(root);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
            var need = j.SourceSizeBytes * 3 + 2L * 1024 * 1024 * 1024;
            if (drive.AvailableFreeSpace < need)
                throw new InvalidOperationException($"Ổ {drive.Name} chỉ còn {drive.AvailableFreeSpace / 1073741824.0:0.0} GB trống — cần khoảng {need / 1073741824.0:0.0} GB. Dọn ổ đĩa hoặc đổi thư mục tạm trong Cài đặt.");
            work = Path.Combine(root, $"{id:N}-{DateTime.UtcNow:HHmmss}");
            Directory.CreateDirectory(work);

            var source = Path.Combine(work, "source.mp4");
            if (!await _files.CopyToAsync(j.SourceBlobId, source, ct)) throw new InvalidOperationException("Video nguồn đã mất khỏi kho file — tải lên lại.");
            var (w, h) = ReupMedia.OutputSize(j.SourceWidth, j.SourceHeight, j.OutputShortSide);
            var fpsValue = ReupMedia.FpsValue(j.SourceFps);
            var duration = preview ? Math.Min(PreviewSeconds, j.SourceDurationSeconds) : j.SourceDurationSeconds;
            var frames = (int)Math.Ceiling(duration * fpsValue);

            var job = new JsonObject
            {
                ["ffmpeg"] = _media.Ffmpeg, ["home"] = _engine.Paths.Home, ["input"] = source, ["output"] = Path.Combine(work, "video.mp4"),
                ["width"] = w, ["height"] = h, ["fps"] = j.SourceFps, ["frames"] = frames,
                ["threads"] = settings.Threads, ["provider"] = settings.ExecutionProvider, ["preset"] = settings.Preset, ["crf"] = settings.Crf
            };
            if (preview) job["durationSeconds"] = duration;
            if (j.FaceSwapEnabled)
            {
                var c = await _db.ReupCharacters.AsNoTracking().FirstAsync(x => x.Id == j.CharacterId, ct);
                var img = Path.Combine(work, "character.img");
                if (!await _files.CopyToAsync(c.ImageBlobId, img, ct)) throw new InvalidOperationException("Ảnh nhân vật đã mất khỏi kho file.");
                job["faceSwap"] = new JsonObject { ["characterImage"] = img };
            }
            var segments = ReadSegments(j.SegmentsJson);
            if (j.BackgroundEnabled)
            {
                var placement = NormalizePlacement(ReadPlacement(j.PlacementJson));
                var ids = segments.Select(s => s.BackdropId).Distinct().ToList();
                var backdrops = await _db.ReupBackdrops.AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
                var local = new Dictionary<Guid, string>();
                foreach (var bid in ids)
                {
                    var b = backdrops[bid];
                    var path = Path.Combine(work, $"backdrop-{local.Count}{(b.Kind == "Video" ? ".mp4" : ".img")}");
                    if (!await _files.CopyToAsync(b.BlobId, path, ct)) throw new InvalidOperationException($"Cảnh \"{b.Name}\" đã mất khỏi kho file.");
                    local[bid] = path;
                }
                var arr = new JsonArray();
                for (var i = 0; i < segments.Count; i++)
                {
                    var end = i + 1 < segments.Count ? segments[i + 1].Start : j.SourceDurationSeconds;
                    arr.Add(new JsonObject
                    {
                        ["start"] = segments[i].Start, ["end"] = end, ["kind"] = backdrops[segments[i].BackdropId].Kind == "Video" ? "video" : "image",
                        ["path"] = local[segments[i].BackdropId], ["kenBurns"] = placement.KenBurns
                    });
                }
                job["background"] = new JsonObject
                {
                    ["segments"] = arr, ["scale"] = placement.Scale, ["offsetX"] = placement.OffsetX, ["offsetY"] = placement.OffsetY,
                    ["harmonize"] = placement.Harmonize, ["lightWrap"] = placement.LightWrap, ["blur"] = placement.Blur, ["crossfade"] = placement.Crossfade
                };
            }
            var jobPath = Path.Combine(work, "job.json");
            await File.WriteAllTextAsync(jobPath, job.ToJsonString(), ct);

            j.FramesTotal = frames;
            j.Stage = "processing";
            j.ProgressText = "Đang nạp model…";
            j.ProgressPercent = 3;
            await _db.SaveChangesAsync(ct);

            var stats = await _engine.RenderAsync(jobPath, settings.MaxRamMb, async progress =>
            {
                var cancel = await _db.ReupJobs.AsNoTracking().Where(x => x.Id == id).Select(x => x.CancelRequested).FirstOrDefaultAsync(CancellationToken.None);
                if (progress != null && progress.Total > 0)
                {
                    j.FramesDone = progress.Done;
                    j.FramesTotal = Math.Max(frames, progress.Total);
                    j.Fps = progress.Fps;
                    j.ProgressPercent = 3 + (int)(85.0 * Math.Min(1, progress.Done / (double)j.FramesTotal));
                    var eta = progress.Fps is > 0 ? TimeSpan.FromSeconds((j.FramesTotal - progress.Done) / progress.Fps.Value) : (TimeSpan?)null;
                    j.ProgressText = $"Đang xử lý khung hình {progress.Done}/{j.FramesTotal}" + (eta is { } e ? $" — còn khoảng {Eta(e)}" : "");
                }
                else if (progress?.Message != null) j.ProgressText = progress.Message;
                await _db.SaveChangesAsync(CancellationToken.None);
                return cancel;
            }, ct);

            j.Stage = "audio";
            j.ProgressText = "Đang xử lý âm thanh…";
            j.ProgressPercent = 90;
            await _db.SaveChangesAsync(ct);
            var video = Path.Combine(work, "video.mp4");
            var final = video;
            if (j.AudioMode != "Remove")
            {
                string? audio = null;
                if (j.AudioBlobId is { } ab)
                {
                    audio = Path.Combine(work, "audio.bin");
                    if (!await _files.CopyToAsync(ab, audio, ct)) throw new InvalidOperationException("File âm thanh đã mất khỏi kho file.");
                }
                final = Path.Combine(work, "final.mp4");
                await _media.MuxAsync(ReupMedia.BuildMuxArgs(j.AudioMode, video, source, audio, duration, 0, j.AudioVolume, j.OriginalVolume, final), ct);
            }

            j.Stage = "verifying";
            j.ProgressText = "Đang kiểm tra video đầu ra…";
            j.ProgressPercent = 94;
            await _db.SaveChangesAsync(ct);
            var checks = await _media.VerifyAsync(final, new ReupMedia.Expectation(duration, fpsValue, w, h, j.AudioMode != "Remove"), ct);
            j.QcReportJson = JsonSerializer.Serialize(checks, Json);
            j.StatsJson = SimpleStats(stats);
            if (checks.FirstOrDefault(c => !c.Ok) is { } bad)
                throw new InvalidOperationException($"Video đầu ra không đạt kiểm tra \"{bad.Name}\": {bad.Detail}");

            j.Stage = "saving";
            j.ProgressText = "Đang lưu video…";
            j.ProgressPercent = 97;
            await _db.SaveChangesAsync(ct);
            var size = new FileInfo(final).Length;
            var blob = await StoreFileAsync(final, "video/mp4", ct);
            if (preview)
            {
                await DeleteBlobAsync(j.PreviewBlobId, ct);
                j.PreviewBlobId = blob.Id;
                j.Status = j.OutputBlobId != null ? "Done" : "Draft";
                j.ProgressText = "Bản xem thử 5 giây đã sẵn sàng.";
            }
            else
            {
                await DeleteBlobAsync(j.OutputBlobId, ct);
                j.OutputBlobId = blob.Id;
                j.OutputDurationSeconds = Math.Round(duration, 3);
                j.OutputSizeBytes = size;
                j.OutputWidth = w;
                j.OutputHeight = h;
                j.Status = "Done";
                j.ProgressText = "Hoàn tất — đã kiểm tra thời lượng, FPS, kích thước, âm thanh và giải mã.";
                j.ExpiresAtUtc = DateTime.UtcNow.AddDays(settings.OutputRetentionDays);
                if (j.BackgroundEnabled)
                {
                    var used = segments.Select(s => s.BackdropId).Distinct().ToList();
                    foreach (var b in await _db.ReupBackdrops.Where(b => used.Contains(b.Id)).ToListAsync(ct)) b.UseCount++;
                }
            }
            j.Stage = "done";
            j.ProgressPercent = 100;
            j.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            if (await CancelRequestedAsync(id))
            {
                j.Status = AfterCancel(j);
                j.Stage = "cancelled";
                j.ProgressText = j.Status == "Done" ? "Đã huỷ lần chạy lại — giữ video kết quả trước." : "Đã huỷ.";
                j.FinishedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
                return;
            }
            // the PC program is stopping (update, shutdown): Hangfire runs the job again when the worker is back
            j.Status = "Queued";
            j.Stage = "queued";
            j.ProgressText = "Máy tính dừng giữa chừng — sẽ tự làm lại khi máy tính chạy lại.";
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reup job {Id} failed", id);
            j.Status = "Failed";
            j.Stage = "failed";
            j.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            j.ProgressText = null;
            j.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            DeleteDir(work);
        }
    }

    /// <summary>A cancelled re-run keeps the previous finished video (still valid): back to Done, else Cancelled.</summary>
    private static string AfterCancel(ReupJob j) => j.OutputBlobId != null && !j.FilesDeleted ? "Done" : "Cancelled";

    private async Task<bool> CancelRequestedAsync(Guid id) =>
        await _db.ReupJobs.AsNoTracking().Where(x => x.Id == id).Select(x => x.CancelRequested).FirstOrDefaultAsync(CancellationToken.None);

    private static string Eta(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} phút {t.Seconds:00}s" : $"{Math.Max(1, (int)t.TotalSeconds)}s";

    private static string SimpleStats(JsonObject stats)
    {
        var d = new Dictionary<string, double>();
        foreach (var (k, v) in stats)
            if (k != "type" && v is JsonValue jv && jv.TryGetValue<double>(out var x)) d[k] = x;
        return JsonSerializer.Serialize(d, Json);
    }

    // ═══════════════════════════════ housekeeping ═══════════════════════════════

    /// <summary>
    /// Every hour (any machine): deletes the source / output / preview / audio files of expired jobs from the bucket
    /// (thumbnails stay for the history), and fails jobs the PC stopped reporting on.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task CleanupAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.ReupJobs.Where(j => !j.FilesDeleted && j.ExpiresAtUtc != null && j.ExpiresAtUtc < now && !Running.Contains(j.Status)).ToListAsync(ct);
        foreach (var j in expired)
        {
            foreach (var b in new[] { j.SourceBlobId, j.AudioBlobId, j.PreviewBlobId, j.OutputBlobId }) await DeleteBlobAsync(b, ct);
            j.AudioBlobId = j.PreviewBlobId = j.OutputBlobId = null;
            j.FilesDeleted = true;
        }
        // drafts nobody touched for 30 days: their source video only takes space
        var stale = now.AddDays(-30);
        foreach (var j in await _db.ReupJobs.Where(j => !j.FilesDeleted && j.ExpiresAtUtc == null && j.Status != "Queued" && j.Status != "Processing"
                     && (j.UpdatedAtUtc ?? j.CreatedAtUtc) < stale).ToListAsync(ct))
        {
            foreach (var b in new[] { j.SourceBlobId, j.AudioBlobId, j.PreviewBlobId, j.OutputBlobId }) await DeleteBlobAsync(b, ct);
            j.AudioBlobId = j.PreviewBlobId = j.OutputBlobId = null;
            j.FilesDeleted = true;
        }
        var silent = now.AddHours(-2);
        foreach (var j in await _db.ReupJobs.Where(j => j.Status == "Processing" && (j.UpdatedAtUtc ?? j.CreatedAtUtc) < silent).ToListAsync(ct))
        {
            j.Status = "Failed";
            j.ErrorMessage = "Máy tính ngừng báo tiến độ hơn 2 giờ (tắt máy / mất mạng giữa chừng) — bấm Làm lại.";
            j.FinishedAtUtc = now;
        }
        await _db.SaveChangesAsync(ct);
        if (expired.Count > 0) _logger.LogInformation("Reup cleanup: deleted the files of {Count} expired videos", expired.Count);
    }

    // ═══════════════════════════════ settings / overview ═══════════════════════════════

    public async Task<ReupSettingsDto> GetSettingsAsync(CancellationToken ct) => ReupSettingsStore.Normalize(await ReupSettingsStore.GetAsync(_db, ct));

    public async Task<ReupSettingsDto> SaveSettingsAsync(ReupSettingsDto input, CancellationToken ct)
    {
        var s = ReupSettingsStore.Normalize(input);
        if (input.SwapperLicenseConfirmed && s.SwapperLicenseNote == null)
            throw new ArgumentException("Ghi rõ giấy phép thương mại của model thay mặt (bên cấp, số hợp đồng / hoá đơn).");
        await ReupSettingsStore.SaveAsync(_db, s, ct);
        return s;
    }

    public async Task<ReupOverviewDto> OverviewAsync(bool pcOnline, string? pcMachine, CancellationToken ct)
    {
        var counts = await _db.ReupJobs.AsNoTracking().GroupBy(j => j.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var minutes = await _db.ReupJobs.AsNoTracking().Where(j => j.Status == "Done").SumAsync(j => (double?)j.OutputDurationSeconds, ct) ?? 0;
        var blobIds = new List<Guid>();
        var jobs = await _db.ReupJobs.AsNoTracking().Select(j => new { j.SourceBlobId, j.FilesDeleted, j.OutputBlobId, j.PreviewBlobId, j.AudioBlobId, j.SourceThumbBlobId }).ToListAsync(ct);
        foreach (var x in jobs)
        {
            if (!x.FilesDeleted) blobIds.Add(x.SourceBlobId);
            foreach (var b in new[] { x.OutputBlobId, x.PreviewBlobId, x.AudioBlobId, x.SourceThumbBlobId }) if (b is { } id) blobIds.Add(id);
        }
        foreach (var c in await _db.ReupCharacters.AsNoTracking().Select(c => new { c.ImageBlobId, c.ThumbBlobId }).ToListAsync(ct))
        {
            blobIds.Add(c.ImageBlobId);
            if (c.ThumbBlobId is { } t) blobIds.Add(t);
        }
        var jobBytes = blobIds.Count == 0 ? 0 : await _db.StudioBlobs.AsNoTracking().Where(b => blobIds.Contains(b.Id)).SumAsync(b => (long?)b.SizeBytes, ct) ?? 0;
        var backdropBytes = await _db.ReupBackdrops.AsNoTracking().SumAsync(b => (long?)b.SizeBytes, ct) ?? 0;
        var settings = await GetSettingsAsync(ct);
        var engine = await ReupSettingsStore.GetEngineAsync(_db, ct);
        var swapBlocker = await FaceSwapBlockerAsync(ct);
        return new ReupOverviewDto
        {
            PcOnline = pcOnline, PcMachine = pcMachine, Engine = engine, FaceSwapAvailable = swapBlocker == null, FaceSwapBlockedReason = swapBlocker,
            Done = counts.GetValueOrDefault("Done"), Running = counts.GetValueOrDefault("Processing"), Queued = counts.GetValueOrDefault("Queued"),
            Failed = counts.GetValueOrDefault("Failed"), Characters = await _db.ReupCharacters.CountAsync(ct), Backdrops = await _db.ReupBackdrops.CountAsync(ct),
            ProcessedMinutes = Math.Round(minutes / 60.0, 1), StorageMb = Math.Round((jobBytes + backdropBytes) / 1048576.0, 1),
            BackdropStorageMb = Math.Round(backdropBytes / 1048576.0, 1), BackdropQuotaMb = settings.BackdropQuotaMb,
            StockSearchPexels = settings.PexelsApiKey != null, StockSearchPixabay = settings.PixabayApiKey != null,
            Recent = (await ListJobsAsync(ct)).Take(6).ToList()
        };
    }

    /// <summary>On the PC: what is installed and how fast it runs (shown on the page; the bench runs only when asked or first time).</summary>
    [Queue(PcQueue)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ProbeEngineAsync(bool bench, CancellationToken ct)
    {
        var settings = ReupSettingsStore.Normalize(await ReupSettingsStore.GetAsync(_db, ct));
        var previous = await ReupSettingsStore.GetEngineAsync(_db, ct);
        var status = new ReupEngineStatusDto
        {
            CheckedAtUtc = DateTime.UtcNow, Machine = Environment.MachineName, Home = _engine.Paths.Home, CpuThreads = Environment.ProcessorCount,
            RamGb = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0, 1), FfmpegPath = _media.Ffmpeg,
            BackgroundFps = previous.Machine == Environment.MachineName ? previous.BackgroundFps : new()
        };
        try
        {
            var root = _engine.Paths.WorkRoot(settings);
            Directory.CreateDirectory(root);
            status.FreeDiskGb = Math.Round(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace / 1073741824.0, 1);
            var probe = await _engine.CommandAsync(new[] { "probe", "--home", _engine.Paths.Home }, TimeSpan.FromMinutes(2), ct);
            status.Python = probe["python"]?.GetValue<string>();
            status.OnnxRuntime = probe["onnxruntime"]?.GetValue<string>();
            status.OpenCv = probe["opencv"]?.GetValue<string>();
            status.Providers = probe["providers"]?.AsArray().Select(p => p!.GetValue<string>()).ToList() ?? new();
            status.Models = probe["models"]?.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value?.GetValue<bool>() == true) ?? new();
            status.Ready = probe["ok"]?.GetValue<bool>() == true;
            status.Error = probe["error"]?.GetValue<string>();
            if (status.Ready && (bench || status.BackgroundFps.Count == 0) && await RunLock.WaitAsync(0, ct))
            {
                try
                {
                    var b = await _engine.CommandAsync(new[] { "bench", "--home", _engine.Paths.Home, "--provider", settings.ExecutionProvider,
                        "--threads", settings.Threads.ToString(Inv) }, TimeSpan.FromMinutes(5), ct);
                    status.BackgroundFps = b["backgroundFps"]?.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<double>()) ?? new();
                }
                finally
                {
                    RunLock.Release();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status.Ready = false;
            status.Error = ex.Message;
        }
        await ReupSettingsStore.SaveEngineAsync(_db, status, CancellationToken.None);
    }

    public void RequestProbe(bool bench) => _jobs.Enqueue<ReupService>(s => s.ProbeEngineAsync(bench, CancellationToken.None));

    /// <summary>On the PC at start: removes work folders left by a run that was cut off (power cut, crash).</summary>
    public void CleanLeftoverWork(ReupSettingsDto settings)
    {
        try
        {
            var root = _engine.Paths.WorkRoot(settings);
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root))
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddHours(-12)) DeleteDir(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogInformation("Reup leftover cleanup: {Message}", ex.Message);
        }
    }
}
