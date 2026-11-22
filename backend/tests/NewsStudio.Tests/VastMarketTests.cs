using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NewsStudio.Infrastructure.FilmFactory;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>Vast.ai offer search / instance parsing (fixture = a real /bundles/ offer from 2026-11-22, trimmed).</summary>
public class VastMarketTests
{
    private const string RealOffer = """
        {"offers": [{"id": 61291673, "gpu_name": "RTX PRO 6000 WS", "num_gpus": 1, "gpu_ram": 97887, "cuda_max_good": 13.2, "dph_total": 1.1222222222222222, "dph_base": 1.0666666666666667, "storage_cost": 0.19999999999999998, "storage_total_cost": 0.05555555555555555, "inet_down_cost": 0.026041666666666668, "inet_down": 927.4, "inet_up": 919.4, "reliability2": 0.959827, "geolocation": "California, US", "cpu_name": "AMD EPYC 7Y83 64-Core Processor", "cpu_cores_effective": 32.0, "cpu_ram": 128980, "disk_space": 521.3073384004881, "disk_bw": 1793.9148498386583, "disk_name": "Micron_7300_MTFDHBE7T6TDF", "dlperf": 265.74439050200004, "duration": 2295638.083636999, "machine_id": 157664, "host_id": 846125, "verification": "verified", "direct_port_count": 31}]}
        """;

    [Fact]
    public void Offer_prices_are_read_with_the_right_units()
    {
        var o = VastAiGpuBackend.ParseOffers(RealOffer, diskGb: 200).Single();

        o.Id.Should().Be(61291673);
        o.GpuName.Should().Be("RTX PRO 6000 WS");
        o.GpuRamGb.Should().BeApproximately(95.6, 0.1);                 // MB -> GB
        o.PricePerHour.Should().BeApproximately(1.1222, 0.0001);         // GPU + 200 GB storage
        o.StoragePerHour.Should().BeApproximately(0.0556, 0.0001);       // 0.20 $/GB/month * 200 GB / 720 h
        o.StoragePerMonth.Should().Be(40.0);                             // what keeping the disk costs per month
        o.ModelDownloadCostUsd.Should().BeApproximately(2.86, 0.01);     // 0.026 $/GB * 110 GB
        o.MaxDurationDays.Should().BeApproximately(26.6, 0.1);           // seconds -> days
        o.RamGb.Should().Be(126);
        // ranking key: (3 h + 0.26 h downloading 110 GB at 927 Mbps) * 1.1222 + 2.86 download fee
        o.SessionCostUsd.Should().BeApproximately(6.53, 0.01);
    }

    [Fact]
    public void Search_body_asks_only_for_machines_that_can_run_the_worker()
    {
        var body = VastAiGpuBackend.BuildSearchBody(new VastOfferQuery(Array.Empty<string>(), DiskGb: 200, MaxDownloadCostPerGb: 0.01));
        var json = JsonSerializer.Serialize(body);

        json.Should().Contain("\"RTX PRO 6000 WS\"").And.Contain("\"RTX PRO 6000 Max-Q\"");
        json.Should().Contain("\"cpu_arch\":{\"eq\":\"amd64\"}");          // GB10/GB200 are ARM
        json.Should().Contain("\"disk_space\":{\"gte\":200}");
        json.Should().Contain("\"allocated_storage\":200");                // prices quoted for this disk
        json.Should().Contain("\"inet_down_cost\":{\"lte\":0.01}");
        json.Should().Contain("\"verified\":{\"eq\":true}");
        json.Should().NotContain("dph_total\":{");                         // no price cap unless asked
    }

    private sealed class FakeVast : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            var body = request.RequestUri!.Query.Contains("after_token=t%2B1")
                ? """{"success":true,"instances":[{"id":2,"actual_status":"exited"}],"next_token":null}"""
                : """{"success":true,"instances":[{"id":1,"actual_status":"running"}],"next_token":"t+1"}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static VastAiGpuBackend Vast(HttpMessageHandler handler) => new(
        new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Film:Gpu:VastAi:ApiKey"] = "test-key" }).Build(),
        new HttpClient(handler),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<VastAiGpuBackend>.Instance);

    [Fact]
    public async Task Instance_list_uses_v1_and_follows_every_page()
    {
        // Vast answers the v0 list with 410 deprecated_endpoint since late 2026.
        var fake = new FakeVast();
        var list = await Vast(fake).ListInstancesAsync();

        list.Select(i => i.Id).Should().Equal("1", "2");
        fake.Urls.Should().HaveCount(2);
        fake.Urls.Should().OnlyContain(u => u.StartsWith("https://console.vast.ai/api/v1/instances/"));
        fake.Urls[1].Should().Contain("after_token=t%2B1");
    }

    [Fact]
    public void Stored_choice_overrides_the_configured_backend_and_instance()
    {
        var before = VastAiGpuBackend.GetRuntimeInstance();
        try
        {
            var vast = Vast(new FakeVast());
            var active = new ActiveFilmGpuBackend(new KaggleGpuBackend(
                new ConfigurationBuilder().Build(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<KaggleGpuBackend>.Instance), vast, null!);

            active.Apply(null, hasInstanceKey: false, vastInstance: null);   // nothing stored yet: config wins
            active.IsVast.Should().BeFalse();

            active.Apply("VastAi", hasInstanceKey: true, vastInstance: "61903314"); // what renting stores
            active.IsVast.Should().BeTrue();
            active.Name.Should().Be("VastAi");
            VastAiGpuBackend.GetRuntimeInstance().Should().Be("61903314");

            active.Apply(null, hasInstanceKey: true, vastInstance: null);     // active machine destroyed
            VastAiGpuBackend.GetRuntimeInstance().Should().BeNull();
            active.IsVast.Should().BeTrue();
        }
        finally
        {
            VastAiGpuBackend.SetRuntimeInstance(before);
        }
    }

    [Fact]
    public void Search_never_lists_machines_below_the_minimum_specs()
    {
        var json = JsonSerializer.Serialize(VastAiGpuBackend.BuildSearchBody(new VastOfferQuery(Array.Empty<string>())));
        var min = VastAiGpuBackend.Requirements.ToDictionary(r => r.Key, r => r.Min);

        json.Should().Contain($"\"gpu_ram\":{{\"gte\":{min["gpuRamGb"] * 1024}}}");
        json.Should().Contain($"\"cpu_ram\":{{\"gte\":{min["ramGb"] * 1024}}}");
        json.Should().Contain($"\"inet_down\":{{\"gte\":{min["downloadMbps"]}}}");
        json.Should().Contain($"\"cpu_cores_effective\":{{\"gte\":{min["cpuCores"]}}}");
        json.Should().Contain($"\"disk_bw\":{{\"gte\":{min["diskMbps"]}}}");
        min["diskGb"].Should().BeLessThanOrEqualTo(new VastOfferQuery(Array.Empty<string>()).DiskGb);
    }

    [Fact]
    public void Account_keeps_only_the_money_fields()
    {
        var a = VastAiGpuBackend.ParseAccount("""
            {"id": 1, "email": "someone@example.com", "api_key": "secret", "ssh_key": "ssh-ed25519 AAA",
             "credit": 23.456, "balance": 0.0, "balance_threshold": 5, "balance_threshold_enabled": true}
            """);

        a.Credit.Should().BeApproximately(23.456, 1e-9);
        a.Balance.Should().Be(0);
        a.AutoBillingEnabled.Should().BeTrue();
        JsonSerializer.Serialize(a).Should().NotContain("secret").And.NotContain("example.com");
    }

    [Fact]
    public void Instances_show_status_ssh_and_monthly_storage()
    {
        const string json = """
            {"instances": [{"id": 61903314, "actual_status": "running", "intended_status": "running", "gpu_name": "RTX PRO 6000 WS",
              "dph_total": 1.656, "disk_space": 190, "storage_cost": 0.47, "public_ipaddr": "5.145.175.98", "geolocation": "Spain, ES",
              "label": "film-factory", "start_date": 1795000000,
              "ports": {"22/tcp": [{"HostIp": "0.0.0.0", "HostPort": "40022"}], "8888/tcp": [{"HostIp": "0.0.0.0", "HostPort": "40888"}]}},
             {"id": 61896771, "actual_status": "exited", "cur_state": "stopped", "gpu_name": "RTX PRO 6000 Max-Q",
              "ssh_host": "ssh4.vast.ai", "ssh_port": 36771}]}
            """;
        var list = VastAiGpuBackend.ParseInstances(json, workerPort: 8888);

        list.Should().HaveCount(2);
        list[0].SshCommand.Should().Be("ssh -p 40022 root@5.145.175.98");
        list[0].WorkerPort.Should().Be(40888);
        list[0].StoragePerMonth.Should().Be(89.3);
        list[1].Status.Should().Be("exited");
        list[1].SshCommand.Should().Be("ssh -p 36771 root@ssh4.vast.ai");
    }
}
