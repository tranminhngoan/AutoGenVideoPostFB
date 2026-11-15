using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Api;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Common;
using Xunit;

namespace NewsStudio.Tests;

[Collection("DatabaseCollection")]
public class Phase8ProductionHardeningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public Phase8ProductionHardeningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void ConnectionStringHelper_Parses_Railway_Postgres_Uri_Format_Accurately()
    {
        // Arrange
        var railwayDatabaseUrl = "postgresql://postgres:secretP%40ssword123@roundhouse.proxy.rlwy.net:45678/railway";

        // Act
        var parsed = ConnectionStringHelper.ParseDatabaseUrl(railwayDatabaseUrl);

        // Assert
        parsed.Should().Contain("Host=roundhouse.proxy.rlwy.net");
        parsed.Should().Contain("Port=45678");
        parsed.Should().Contain("Database=railway");
        parsed.Should().Contain("Username=postgres");
        parsed.Should().Contain("Password=secretP@ssword123");
    }

    [Fact]
    public async Task JobRecoveryService_Reconciles_Dangling_Rendering_Jobs_When_Files_Missing()
    {
        // Arrange: Worker crashed while rendering a video, leaving status = Rendering and asset missing
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var recoveryService = scope.ServiceProvider.GetRequiredService<IJobRecoveryService>();

        var project = new VideoProject
        {
            Title = "Dự án bị sập giữa chừng khi render",
            SourceUrl = "https://example.com/crash-test",
            Status = VideoProjectStatus.Rendering
        };
        db.VideoProjects.Add(project);

        var missingPath = Path.Combine(Path.GetTempPath(), $"missing_vid_{Guid.NewGuid():N}.mp4");
        db.VideoAssets.Add(new VideoAsset
        {
            VideoProjectId = project.Id,
            AssetType = "FinalVideo",
            LocalTempPath = missingPath, // file does NOT exist
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            IsCleanedUp = false,
            Status = "Active"
        });

        db.ProcessingJobs.Add(new ProcessingJob
        {
            VideoProjectId = project.Id,
            Step = "Rendering",
            Status = "Running"
        });

        await db.SaveChangesAsync();

        // Act: Worker starts up and executes JobRecoveryService
        var report = await recoveryService.RecoverDanglingJobsAsync();

        // Assert
        report.Should().NotBeNull();
        report.TotalDanglingProjectsExamined.Should().BeGreaterOrEqualTo(1);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var updatedProject = await verifyDb.VideoProjects.FindAsync(project.Id);

        updatedProject!.Status.Should().Be(VideoProjectStatus.Failed);
        updatedProject.ErrorMessage.Should().Contain("ngắt quãng");

        var updatedJob = verifyDb.ProcessingJobs.FirstOrDefault(j => j.VideoProjectId == project.Id);
        updatedJob!.Status.Should().Be("Failed");
    }

    [Fact]
    public async Task JobRecoveryService_Protects_Already_Published_Jobs_From_Duplicate_Publish()
    {
        // Arrange: Interrupted job in Publishing state, but post was already pushed to Facebook
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var recoveryService = scope.ServiceProvider.GetRequiredService<IJobRecoveryService>();

        var project = new VideoProject
        {
            Title = "Dự án bị restart sau khi đẩy lên Facebook",
            SourceUrl = "https://example.com/publish-protect",
            Status = VideoProjectStatus.Publishing
        };
        db.VideoProjects.Add(project);

        db.FacebookPosts.Add(new FacebookPost
        {
            VideoProjectId = project.Id,
            FacebookPostId = "page_123_post_456",
            FacebookVideoId = "vid_456",
            UploadStatus = "Published",
            PublishedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Status = "Published"
        });

        await db.SaveChangesAsync();

        // Act
        var report = await recoveryService.RecoverDanglingJobsAsync();

        // Assert: Never re-publish; reconcile to Published
        report.ProtectedPublishedProjectsCount.Should().BeGreaterOrEqualTo(1);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var updatedProject = await verifyDb.VideoProjects.FindAsync(project.Id);
        updatedProject!.Status.Should().Be(VideoProjectStatus.Published);
    }

    [Fact]
    public async Task JobRecoveryService_Advances_To_Review_When_Intact_Video_Found()
    {
        // Arrange: Worker restarted, but generated MP4 file is still intact on disk
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var recoveryService = scope.ServiceProvider.GetRequiredService<IJobRecoveryService>();

        var project = new VideoProject
        {
            Title = "Dự án render xong nhưng chưa kịp lưu AwaitingApproval",
            SourceUrl = "https://example.com/intact-test",
            Status = VideoProjectStatus.Rendering
        };
        db.VideoProjects.Add(project);

        var tempVideoPath = Path.Combine(Path.GetTempPath(), $"intact_vid_{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(tempVideoPath, new byte[] { 0x00, 0x00, 0x00, 0x18 });

        db.VideoAssets.Add(new VideoAsset
        {
            VideoProjectId = project.Id,
            AssetType = "FinalVideo",
            LocalTempPath = tempVideoPath,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            IsCleanedUp = false,
            Status = "Active"
        });
        await db.SaveChangesAsync();

        // Act
        var report = await recoveryService.RecoverDanglingJobsAsync();

        // Assert
        report.RecoveredToAwaitingApprovalCount.Should().BeGreaterOrEqualTo(1);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var updatedProject = await verifyDb.VideoProjects.FindAsync(project.Id);
        updatedProject!.Status.Should().Be(VideoProjectStatus.AwaitingApproval);

        // Cleanup test file
        if (File.Exists(tempVideoPath)) File.Delete(tempVideoPath);
    }

    [Fact]
    public async Task StorageCleanupService_Purges_Expired_Assets_And_Enforces_Quota()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var cleanupService = scope.ServiceProvider.GetRequiredService<IStorageCleanupService>();

        var project = new VideoProject
        {
            Title = "Dự án kiểm tra dọn dẹp storage",
            SourceUrl = "https://example.com/storage-cleanup-test",
            Status = VideoProjectStatus.Published
        };
        db.VideoProjects.Add(project);

        var expiredFilePath = Path.Combine(Path.GetTempPath(), $"expired_asset_{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(expiredFilePath, new byte[1024]); // 1 KB dummy file

        var expiredAsset = new VideoAsset
        {
            VideoProjectId = project.Id,
            AssetType = "VoiceOverAudio",
            LocalTempPath = expiredFilePath,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(-1), // Expired 1 hour ago
            IsCleanedUp = false,
            Status = "Active"
        };
        db.VideoAssets.Add(expiredAsset);
        await db.SaveChangesAsync();

        // Act: Run storage cleanup
        var report = await cleanupService.RunStorageCleanupAsync();

        // Assert
        report.Should().NotBeNull();
        report.ExpiredAssetsCleanedCount.Should().BeGreaterOrEqualTo(1);
        report.StorageWithinLimits.Should().BeTrue();

        // Verify physical file was deleted
        File.Exists(expiredFilePath).Should().BeFalse();

        // Verify database marked as cleaned up
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var updatedAsset = await verifyDb.VideoAssets.FindAsync(expiredAsset.Id);
        updatedAsset!.IsCleanedUp.Should().BeTrue();
        updatedAsset.Status.Should().Be("CleanedUp");
    }

    [Fact]
    public async Task SystemMetricsService_Computes_Cpu_Memory_Storage_And_Cost_Aggregations()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var metricsService = scope.ServiceProvider.GetRequiredService<ISystemMetricsService>();

        // Act
        var metrics = await metricsService.GetSystemMetricsAsync();
        var costs = await metricsService.GetCostSummaryAsync();

        // Assert: System Telemetry
        metrics.Should().NotBeNull();
        metrics.MemoryWorkingSetMb.Should().BeGreaterThan(0);
        metrics.TempStorageLimitMb.Should().BeGreaterThan(0);
        metrics.DatabaseStatus.Should().Be("Connected");
        metrics.DatabaseLatencyMs.Should().BeGreaterOrEqualTo(0);
        metrics.Uptime.TotalSeconds.Should().BeGreaterThan(0);

        // Assert: Cost Summary
        costs.Should().NotBeNull();
        costs.TotalAiCostUsd.Should().BeGreaterOrEqualTo(0);
        costs.TotalProjectsCount.Should().BeGreaterOrEqualTo(0);
        costs.PreferredAiProvider.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SystemController_Endpoints_Full_Workflow()
    {
        // 1. Act: GET /api/system/metrics
        var metricsRes = await _client.GetAsync("/api/system/metrics");
        metricsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var metricsDto = await metricsRes.Content.ReadFromJsonAsync<SystemMetricsDto>();
        metricsDto.Should().NotBeNull();
        metricsDto!.MemoryWorkingSetMb.Should().BeGreaterThan(0);

        // 2. Act: GET /api/system/costs
        var costsRes = await _client.GetAsync("/api/system/costs");
        costsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var costsDto = await costsRes.Content.ReadFromJsonAsync<SystemCostSummaryDto>();
        costsDto.Should().NotBeNull();
        costsDto!.AverageCostPerProjectUsd.Should().BeGreaterOrEqualTo(0);

        // 3. Act: POST /api/system/storage/cleanup
        var cleanupRes = await _client.PostAsync("/api/system/storage/cleanup", null);
        cleanupRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var cleanupDto = await cleanupRes.Content.ReadFromJsonAsync<StorageCleanupReportDto>();
        cleanupDto.Should().NotBeNull();
        cleanupDto!.StorageWithinLimits.Should().BeTrue();

        // 4. Act: POST /api/system/jobs/recover
        var recoverRes = await _client.PostAsync("/api/system/jobs/recover", null);
        recoverRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var recoverDto = await recoverRes.Content.ReadFromJsonAsync<JobRecoveryReportDto>();
        recoverDto.Should().NotBeNull();
        recoverDto!.ExecutedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ProductionHardening_Simulates_Multi_Video_And_Concurrent_Jobs_Safely()
    {
        // Arrange: 3 consecutive projects with 2 concurrent simulations
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var projects = new List<VideoProject>();
        for (int i = 1; i <= 3; i++)
        {
            var p = new VideoProject
            {
                Title = $"Bản tin thử nghiệm tải #{i} trên Railway",
                SourceUrl = $"https://example.com/multi-video-test-{i}",
                Publisher = "Báo Công Nghệ",
                Status = VideoProjectStatus.Pending
            };
            db.VideoProjects.Add(p);
            projects.Add(p);
        }
        await db.SaveChangesAsync();

        // Act: Execute parallel lightweight steps on projects
        var tasks = projects.Select(async p =>
        {
            using var subScope = _factory.Services.CreateScope();
            var subDb = subScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var target = await subDb.VideoProjects.FindAsync(p.Id);
            target!.Status = VideoProjectStatus.Analyzing;
            await subDb.SaveChangesAsync();

            target.Status = VideoProjectStatus.Scripting;
            await subDb.SaveChangesAsync();

            target.Status = VideoProjectStatus.AwaitingApproval;
            await subDb.SaveChangesAsync();
        });

        await Task.WhenAll(tasks);

        // Assert: All 3 projects completed transitions without deadlock
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        foreach (var p in projects)
        {
            var verified = await verifyDb.VideoProjects.FindAsync(p.Id);
            verified!.Status.Should().Be(VideoProjectStatus.AwaitingApproval);
        }
    }
}
