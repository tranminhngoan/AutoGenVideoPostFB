namespace NewsStudio.Application.Interfaces;

public interface IVideoProcessingJobExecutor
{
    Task ExecuteVideoProcessingPipelineAsync(Guid projectId, string idempotencyKey, CancellationToken cancellationToken);
}
