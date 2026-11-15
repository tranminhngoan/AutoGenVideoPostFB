using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NewsStudio.Tests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RootEndpoint_ReturnsHealthyStatusAndDocsUrl()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("AI News Studio API");
        content.Should().Contain("/swagger");
    }

    [Fact]
    public async Task StandardHealthEndpoints_ReturnSuccess()
    {
        // Act & Assert /health
        var healthResponse = await _client.GetAsync("/health");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert /health/ready
        var readyResponse = await _client.GetAsync("/health/ready");
        readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert /health/live
        var liveResponse = await _client.GetAsync("/health/live");
        liveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiHealthController_ReturnsDetailedJson()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("version").GetString().Should().Be("1.0.0");
        root.GetProperty("components").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidJsonSpec()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("AI News Studio API");
    }

    [Fact]
    public async Task SystemConfigSummary_DoesNotExposeSecrets()
    {
        // Act
        var response = await _client.GetAsync("/api/system/config-summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.TryGetProperty("ai", out var ai).Should().BeTrue();
        ai.TryGetProperty("apiKey", out _).Should().BeFalse(); // No API key exposed!

        root.TryGetProperty("facebook", out var fb).Should().BeTrue();
        fb.TryGetProperty("pageAccessToken", out _).Should().BeFalse(); // No token exposed!
    }
}
