using System.Net;
using Microsoft.Extensions.Configuration;
using NewsStudio.Infrastructure.Common;
using Xunit;

namespace NewsStudio.Tests;

public class GeminiKeysTests
{
    private static IConfiguration Config(string? free, string? paid) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Gemini:FreeApiKey"] = free,
            ["Ai:Gemini:ApiKey"] = paid
        })
        .Build();

    // Each test uses its own model name: the "free key is resting" state is process-wide.
    private static string Model() => "test-model-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void FreeKeyIsTriedBeforePaidKey()
    {
        var keys = GeminiKeys.InOrder(Config("free-key", "paid-key"), Model());

        Assert.Equal(new[] { "free-key", "paid-key" }, keys.Select(k => k.Value));
        Assert.True(keys[0].IsFree);
        Assert.False(keys[1].IsFree);
    }

    [Fact]
    public void BlankSettingsMeanNoKey()
    {
        Assert.False(GeminiKeys.HasAny(Config(" ", " ")));
    }

    [Fact]
    public void SameKeyInBothSettingsIsNotCalledTwice()
    {
        var keys = GeminiKeys.InOrder(Config("same", "same"), Model());

        var only = Assert.Single(keys);
        Assert.False(only.IsFree);
    }

    [Fact]
    public void FreeKeyRestsAfterDailyQuotaIsUsedUp()
    {
        var model = Model();
        var config = Config("free-key", "paid-key");

        GeminiKeys.MarkFreeRefused(model, HttpStatusCode.TooManyRequests,
            "Quota exceeded for metric: generate_content_free_tier_requests, limit: 10, quotaId: GenerateRequestsPerDayPerProjectPerModel-FreeTier");

        var only = Assert.Single(GeminiKeys.InOrder(config, model));
        Assert.Equal("paid-key", only.Value);
        // Other models keep using the free key
        Assert.Equal(2, GeminiKeys.InOrder(config, Model()).Count);
    }

    [Fact]
    public void ServerErrorDoesNotRestTheFreeKey()
    {
        var model = Model();
        GeminiKeys.MarkFreeRefused(model, HttpStatusCode.ServiceUnavailable, "The model is overloaded.");

        Assert.Equal(2, GeminiKeys.InOrder(Config("free-key", "paid-key"), model).Count);
    }

    [Fact]
    public void OnlyFreeKeyIsStillUsedWhileResting()
    {
        var model = Model();
        GeminiKeys.MarkFreeRefused(model, HttpStatusCode.TooManyRequests, "PerDay");

        // Blank paid setting = no paid key (and no fallback to a machine-wide GEMINI_API_KEY)
        var only = Assert.Single(GeminiKeys.InOrder(Config("free-key", " "), model));
        Assert.True(only.IsFree);
    }
}
