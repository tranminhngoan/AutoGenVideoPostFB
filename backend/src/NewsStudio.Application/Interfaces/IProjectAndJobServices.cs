using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

public interface IVideoProjectService
{
    Task<VideoProjectResponseDto> CreateProjectAsync(CreateVideoProjectDto dto, CancellationToken cancellationToken = default);
    Task<IEnumerable<VideoProjectResponseDto>> GetProjectsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<VideoProjectResponseDto?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProcessingJobResponseDto> EnqueueProjectProcessingAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public interface IProcessingJobService
{
    Task<ProcessingJobResponseDto?> GetJobByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> CancelJobAsync(Guid id, CancellationToken cancellationToken = default);
}
