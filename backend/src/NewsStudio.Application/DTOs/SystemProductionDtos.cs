using System;
using System.Collections.Generic;

namespace NewsStudio.Application.DTOs;

public class SystemMetricsDto
{
    public double CpuUsagePercent { get; set; }
    public double MemoryWorkingSetMb { get; set; }
    public double TempStorageUsedMb { get; set; }
    public double TempStorageLimitMb { get; set; }
    public double TempStorageUsagePercent { get; set; }
    public string TempStoragePath { get; set; } = string.Empty;
    public int ActiveJobsCount { get; set; }
    public int PendingJobsCount { get; set; }
    public int CompletedJobsCount { get; set; }
    public int FailedJobsCount { get; set; }
    public string DatabaseStatus { get; set; } = "Connected";
    public double DatabaseLatencyMs { get; set; }
    public TimeSpan Uptime { get; set; }
    public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
    public string Environment { get; set; } = "Production";
}

public class SystemCostSummaryDto
{
    public decimal TotalAiCostUsd { get; set; }
    public decimal TotalTtsCostUsd { get; set; }
    public decimal TotalCostUsd => TotalAiCostUsd + TotalTtsCostUsd;
    public int TotalProjectsCount { get; set; }
    public int PublishedProjectsCount { get; set; }
    public decimal AverageCostPerProjectUsd { get; set; }
    public long TotalTokensUsed { get; set; }
    public long TotalTtsCharactersSynthesized { get; set; }
    public string PreferredAiProvider { get; set; } = "Gemini";
    public string PreferredTtsProvider { get; set; } = "EdgeTTS";
}

public class JobRecoveryReportDto
{
    public int TotalDanglingProjectsExamined { get; set; }
    public int RecoveredToAwaitingApprovalCount { get; set; }
    public int ResetToPendingCount { get; set; }
    public int MarkedFailedCount { get; set; }
    public int ProtectedPublishedProjectsCount { get; set; }
    public List<string> ActionLogs { get; set; } = new();
    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
}

public class StorageCleanupReportDto
{
    public int ExpiredAssetsCleanedCount { get; set; }
    public int OrphanedDirectoriesRemovedCount { get; set; }
    public double TotalBytesFreedMb { get; set; }
    public double CurrentStorageUsedMb { get; set; }
    public double StorageLimitMb { get; set; }
    public bool StorageWithinLimits { get; set; }
    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
}
