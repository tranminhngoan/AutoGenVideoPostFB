using NewsStudio.Infrastructure.Common;
using NewsStudio.Infrastructure.Rendering;
using Xunit;

namespace NewsStudio.Tests;

public class GeminiTtsModelFallbackTests
{
    [Fact]
    public void OrderTtsModels_FlashStableNewestFirst_ProLast()
    {
        var ordered = GeminiTtsService.OrderTtsModels(new[]
        {
            "gemini-2.5-pro-preview-tts",
            "gemini-2.5-flash-preview-tts",
            "gemini-3.8-flash-tts",
            "gemini-3.5-flash-tts",
            "gemini-3.8-flash-lite-tts",
            "gemini-3.8-flash",
        });

        Assert.Equal(new[]
        {
            "gemini-3.8-flash-tts",
            "gemini-3.5-flash-tts",
            "gemini-3.8-flash-lite-tts",
            "gemini-2.5-flash-preview-tts",
            "gemini-2.5-pro-preview-tts",
        }, ordered);
    }

    [Theory]
    [InlineData("Quota exceeded for metric: generate_requests_per_model_per_day", true)]
    [InlineData("Quota exceeded for quota id GenerateRequestsPerDayPerProjectPerModel", true)]
    [InlineData("You exceeded your current quota. Please retry in 7h12m30s.", true)]
    [InlineData("Please retry in 37.5s.", false)]
    [InlineData("Quota exceeded for metric: generate_requests_per_model_per_minute", false)]
    public void IsDailyQuota_DetectsQuotasThatWontClearInMinutes(string reason, bool daily)
    {
        Assert.Equal(daily, GeminiKeys.IsDailyQuota(reason));
    }
}
