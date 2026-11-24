using FluentAssertions;
using Hangfire;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Api;
using NewsStudio.Api.Workers;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Common;
using Xunit;

namespace NewsStudio.Tests;

[Collection("DatabaseCollection")]
public class WorkerSwitchTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WorkerSwitchTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task The_switch_server_runs_jobs_through_dependency_injection()
    {
        var services = _factory.Services;
        var queue = "switchtest" + Guid.NewGuid().ToString("N")[..8];
        var client = services.GetRequiredService<IBackgroundJobClient>();
        // an interface job resolved from DI, like every real job (IEngagementService reads the database)
        var id = client.Create<IEngagementService>(s => s.FirstCommentEnabledAsync(CancellationToken.None), new EnqueuedState(queue));
        using (var server = WorkerSwitchService.CreateServer(services, new BackgroundJobServerOptions { Queues = new[] { queue }, WorkerCount = 1, ServerName = "switch-test" }))
        {
            var monitor = JobStorage.Current.GetMonitoringApi();
            string? state = null;
            for (var i = 0; i < 60 && state != "Succeeded" && state != "Failed"; i++)
            {
                await Task.Delay(500);
                state = monitor.JobDetails(id)?.History?.FirstOrDefault()?.StateName;
            }
            state.Should().Be("Succeeded");
            server.SendStop();
        }
    }

    [Fact]
    public void S3_signature_matches_the_aws_documentation_example()
    {
        // "GET Object" example of the AWS Signature Version 4 documentation
        var s3 = S3Storage.ForTest("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "us-east-1");
        var headers = new Dictionary<string, string>
        {
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["range"] = "bytes=0-9",
            ["x-amz-content-sha256"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ["x-amz-date"] = "20130524T000000Z"
        };
        var auth = s3.Sign("GET", "/test.txt", "", headers, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            new DateTime(2013, 5, 24, 0, 0, 0, DateTimeKind.Utc));
        auth.Should().Be("AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, " +
                         "SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, " +
                         "Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41");
    }

    [Fact]
    public void Cloud_hands_over_only_to_a_live_pc_running_the_same_version()
    {
        var now = DateTime.UtcNow;
        WorkerSwitchService.Beat Pc(int secondsAgo, string version) => new() { HeartbeatUtc = now.AddSeconds(-secondsAgo), Version = version };
        WorkerSwitchService.PcShouldWork(Pc(20, "abc"), "abc", now).Should().BeTrue();
        WorkerSwitchService.PcShouldWork(Pc(200, "abc"), "abc", now).Should().BeFalse();   // PC switched off / crashed
        WorkerSwitchService.PcShouldWork(Pc(20, "old"), "abc", now).Should().BeFalse();    // PC runs an older copy
        WorkerSwitchService.PcShouldWork(new WorkerSwitchService.Beat { HeartbeatUtc = null, Version = "abc" }, "abc", now).Should().BeFalse(); // PC stopped cleanly
        WorkerSwitchService.PcShouldWork(null, "abc", now).Should().BeFalse();             // no PC at all
    }

    [Fact]
    public void WhoWorks_ShowsWhichMachineRunsTheJobs()
    {
        var now = DateTime.UtcNow;
        WorkerSwitchService.Beat B(int secondsAgo, bool working) => new() { HeartbeatUtc = now.AddSeconds(-secondsAgo), Working = working, Version = "abc" };
        WorkerSwitchService.WhoWorks(B(10, true), B(10, false), now).Should().Be("pc");
        WorkerSwitchService.WhoWorks(B(10, false), B(10, true), now).Should().Be("cloud");
        WorkerSwitchService.WhoWorks(null, B(10, true), now).Should().Be("cloud");
        WorkerSwitchService.WhoWorks(B(300, true), B(10, true), now).Should().Be("cloud");  // PC switched off without saying so
        WorkerSwitchService.WhoWorks(B(10, true), B(10, true), now).Should().Be("both");    // cloud finishing a job before handing over
        WorkerSwitchService.WhoWorks(B(300, true), B(300, true), now).Should().Be("none");
    }
}
