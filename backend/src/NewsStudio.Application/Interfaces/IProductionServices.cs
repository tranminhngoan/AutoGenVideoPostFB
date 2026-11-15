using System.Threading;
using System.Threading.Tasks;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

public interface IJobRecoveryService
{
    /// <summary>
    /// Scans database for interrupted or dangling jobs following a worker crash or restart.
    /// Reconciles state, checks local ephemeral file existence, safely recovers or requeues jobs,
    /// and strictly guarantees NO duplicate publishing.
    /// </summary>
    Task<JobRecoveryReportDto> RecoverDanglingJobsAsync(CancellationToken cancellationToken = default);
}

public interface ISystemMetricsService
{
    /// <summary>
    /// Gathers live operational metrics: CPU, RAM, Temp Storage disk usage, Job queue counts, and DB latency.
    /// </summary>
    Task<SystemMetricsDto> GetSystemMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gathers historical resource and cost usage across AI and TTS providers.
    /// </summary>
    Task<SystemCostSummaryDto> GetCostSummaryAsync(CancellationToken cancellationToken = default);
}

public interface IStorageCleanupService
{
    /// <summary>
    /// Scans ephemeral storage path, purges expired assets based on TTL and enforces maximum disk quota.
    /// </summary>
    Task<StorageCleanupReportDto> RunStorageCleanupAsync(CancellationToken cancellationToken = default);
}
