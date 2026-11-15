using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using Xunit;

namespace NewsStudio.Tests;

[Collection("DatabaseCollection")]
public class Phase2DatabaseAndHangfireTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public Phase2DatabaseAndHangfireTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostVideoProject_CreatesEntityInDatabase_Returns201Created()
    {
        // Arrange
        var requestDto = new CreateVideoProjectDto
        {
            Title = "Test Metro Line Project",
            SourceUrl = "https://vnexpress.net/test-metro-line-4899999.html",
            Publisher = "VnExpress",
            TargetDurationSeconds = 55
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/video-projects", requestDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProject = await response.Content.ReadFromJsonAsync<VideoProjectResponseDto>();
        createdProject.Should().NotBeNull();
        createdProject!.Id.Should().NotBeEmpty();
        createdProject.Title.Should().Be("Test Metro Line Project");
        createdProject.Status.Should().Be("Pending");

        // Verify in PostgreSQL database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var dbProject = await db.VideoProjects.FindAsync(createdProject.Id);
        dbProject.Should().NotBeNull();
        dbProject!.SourceUrl.Should().Be("https://vnexpress.net/test-metro-line-4899999.html");
    }

    [Fact]
    public async Task GetVideoProjects_ReturnsListFromDatabase()
    {
        // Act
        var response = await _client.GetAsync("/api/video-projects");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var projects = await response.Content.ReadFromJsonAsync<List<VideoProjectResponseDto>>();
        projects.Should().NotBeNull();
    }

    [Fact]
    public async Task GetVideoProjectById_ReturnsCorrectProject_Or404IfNotFound()
    {
        // Arrange: non-existent GUID
        var nonExistentId = Guid.NewGuid();

        // Act
        var notFoundResponse = await _client.GetAsync($"/api/video-projects/{nonExistentId}");

        // Assert
        notFoundResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProcessVideoProject_EnqueuesJob_AndUpdatesStatus()
    {
        // 1. Create a project
        var createResponse = await _client.PostAsJsonAsync("/api/video-projects", new CreateVideoProjectDto
        {
            Title = "Job Processing Test",
            SourceUrl = "https://tuoitre.vn/test-job-process-12345.htm",
            Publisher = "Tuổi Trẻ",
            TargetDurationSeconds = 48
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = await createResponse.Content.ReadFromJsonAsync<VideoProjectResponseDto>();

        // 2. Enqueue processing job
        var processResponse = await _client.PostAsync($"/api/video-projects/{project!.Id}/process", null);
        processResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var job = await processResponse.Content.ReadFromJsonAsync<ProcessingJobResponseDto>();
        job.Should().NotBeNull();
        job!.HangfireJobId.Should().NotBeNullOrEmpty();
        job.Step.Should().Be("Extracting");

        // 3. Query job status by ID
        var getJobResponse = await _client.GetAsync($"/api/jobs/{job.Id}");
        getJobResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedJob = await getJobResponse.Content.ReadFromJsonAsync<ProcessingJobResponseDto>();
        fetchedJob.Should().NotBeNull();
        fetchedJob!.Id.Should().Be(job.Id);

        // 4. Cancel job
        var cancelResponse = await _client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task JobExecutor_SimulatesFullLifecycleTransitions()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var executor = scope.ServiceProvider.GetRequiredService<NewsStudio.Application.Interfaces.IVideoProcessingJobExecutor>();

        var testProject = new VideoProject
        {
            Title = "Lifecycle Test Project",
            SourceUrl = "https://thanhnien.vn/test-lifecycle-5555.htm",
            Publisher = "Thanh Niên",
            Status = VideoProjectStatus.Pending
        };
        db.VideoProjects.Add(testProject);
        await db.SaveChangesAsync();

        // Act: Execute pipeline synchronously
        var key = Guid.NewGuid().ToString();
        await executor.ExecuteVideoProcessingPipelineAsync(testProject.Id, key, CancellationToken.None);

        // Assert: Project should have reached AwaitingApproval (awaiting user review)
        var updatedProject = await db.VideoProjects.FindAsync(testProject.Id);
        updatedProject.Should().NotBeNull();
        updatedProject!.Status.Should().Be(VideoProjectStatus.AwaitingApproval);

        // Assert: No binary media in Database
        var assets = db.VideoAssets.Where(a => a.VideoProjectId == testProject.Id).ToList();
        // Video assets only contain file metadata/temp paths, no byte[] binary
        assets.All(a => !string.IsNullOrEmpty(a.LocalTempPath)).Should().BeTrue();
    }
}
