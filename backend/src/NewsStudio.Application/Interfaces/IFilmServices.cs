using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

public interface IFilmFactoryService
{
    Task<List<FilmPageDto>> GetFilmPagesAsync(CancellationToken ct = default);
    Task<FilmPageDto?> GetFilmPageByIdAsync(Guid pageId, CancellationToken ct = default);
    Task<FilmPageDto> UpdateFilmPageAsync(Guid pageId, UpdateFilmPageRequestDto request, CancellationToken ct = default);
    Task SeedDefaultFilmPagesAsync(CancellationToken ct = default);

    Task<List<FilmProjectDto>> GetProjectsAsync(Guid? pageId = null, CancellationToken ct = default);
    Task<FilmProjectDetailDto?> GetProjectByIdAsync(Guid projectId, CancellationToken ct = default);
    Task<FilmProjectDto> CreateProjectAsync(CreateFilmProjectRequestDto request, CancellationToken ct = default);
    Task<bool> DeleteProjectAsync(Guid projectId, CancellationToken ct = default);

    Task<FilmStoryBibleDto?> GetStoryBibleAsync(Guid projectId, CancellationToken ct = default);
    Task<List<FilmCharacterDto>> GetCharactersAsync(Guid projectId, CancellationToken ct = default);
    Task<FilmCharacterDto?> GetCharacterByIdAsync(Guid characterId, CancellationToken ct = default);
    Task<FilmCharacterDto> UpdateCharacterAsync(Guid characterId, UpdateFilmCharacterRequestDto request, CancellationToken ct = default);
    Task<FilmCharacterDto> SetCharacterPortraitPathAsync(Guid characterId, string imagePath, CancellationToken ct = default);
    Task<FilmCharacterDto> GenerateCharacterPortraitAsync(Guid characterId, CancellationToken ct = default);
    Task<List<FilmLocationDto>> GetLocationsAsync(Guid projectId, CancellationToken ct = default);
    Task<List<FilmEpisodeDto>> GetEpisodesAsync(Guid projectId, CancellationToken ct = default);
    Task<FilmEpisodeDetailDto?> GetEpisodeDetailAsync(Guid episodeId, CancellationToken ct = default);
    Task<FilmCostSummaryDto> GetCostSummaryAsync(Guid projectId, CancellationToken ct = default);
}

public interface IFilmShowrunnerService
{
    /// <summary>
    /// Phát triển ý tưởng thô thành Series Bible hoàn chỉnh (Thế giới, xung đột, bí mật, tóm tắt 10 tập kết nối).
    /// </summary>
    Task<FilmStoryBibleDto> DevelopStoryBibleAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Tạo hồ sơ các nhân vật và bối cảnh chính với các prompt giữ nhất quán khuôn mặt và ánh sáng.
    /// </summary>
    Task<(List<FilmCharacterDto> Characters, List<FilmLocationDto> Locations)> GenerateCharactersAndLocationsAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Viết kịch bản chi tiết cho 1 tập phim (đạt chuẩn thời lượng 5 phút = 300s, phân rã Scene & Shot, lời thoại, góc máy).
    /// </summary>
    Task<FilmEpisodeDetailDto> WriteEpisodeScriptAsync(Guid episodeId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra mâu thuẫn cốt truyện, trang phục, bối cảnh giữa tập N và các tập trước đó (Continuity Checker).
    /// </summary>
    Task<bool> CheckContinuityAsync(Guid episodeId, CancellationToken ct = default);
}

public interface IFilmCostController
{
    Task<bool> CanExecutePaidTaskAsync(Guid projectId, decimal estimatedCostUsd, CancellationToken ct = default);
    Task RecordCostAsync(Guid projectId, Guid? episodeId, Guid? shotId, string provider, string serviceType, long units, decimal costUsd, bool isFree, string? note = null, CancellationToken ct = default);
    Task<FilmCostSummaryDto> EvaluateBudgetStatusAsync(Guid projectId, CancellationToken ct = default);
}

public interface IFilmRendererService
{
    /// <summary>
    /// Tạo video cho 1 shot ngắn (3–8s): Sinh audio thoại qua EdgeTTS + Tạo ảnh/video + Áp dụng chuyển động máy quay.
    /// </summary>
    Task<string> RenderShotAsync(Guid shotId, CancellationToken ct = default);

    /// <summary>
    /// Ghép toàn bộ các shot trong tập thành file video hoàn chỉnh 5 phút bằng FFmpeg concat, chèn BGM và phụ đề karaoke.
    /// </summary>
    Task<string> AssembleEpisodeAsync(Guid episodeId, CancellationToken ct = default);

    /// <summary>
    /// Lồng tiếng toàn bộ câu thoại của tập trước (để biết thời lượng thật từng shot trước khi gửi lên máy GPU).
    /// </summary>
    Task PrepareVoicesAsync(Guid episodeId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra chất lượng kỹ thuật của tập video xuất xưởng (Codec, độ phân giải, âm thanh, độ dài).
    /// </summary>
    Task<double> CheckQualityAsync(Guid episodeId, string videoPath, CancellationToken ct = default);
}

public interface IFilmProductionCoordinator
{
    /// <summary>
    /// Kích hoạt chuỗi sản xuất tự động toàn bộ series 10 tập (Chạy ngầm qua Hangfire).
    /// </summary>
    Task TriggerFullAutoProductionAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Tiến trình từng bước được gọi từ background worker.
    /// </summary>
    Task ExecuteProductionPipelineAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Đưa 1 tập vào hàng đợi dựng (chạy nền): viết kịch bản nếu chưa có, tạo hình + giọng, ghép MP4.
    /// </summary>
    Task TriggerEpisodeRenderAsync(Guid episodeId, bool force = false, CancellationToken ct = default);

    /// <summary>
    /// Job nền dựng 1 tập phim.
    /// </summary>
    Task RenderEpisodeJobAsync(Guid episodeId, CancellationToken ct = default);
}
