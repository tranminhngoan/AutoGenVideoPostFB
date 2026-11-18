using NewsStudio.Infrastructure.Ai;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>Numbers flagged as "not in the article" on production although the article had them.</summary>
public class FactCheckNumbersTests
{
    private static List<string> Missing(string voiceOver, string reference) =>
        FactCheck.NumbersNotIn(voiceOver, FactCheck.NumberVariants(reference));

    [Fact]
    public void VietnameseDecimalCommaSpokenAsTwoNumbers()
    {
        var article = "Đội tuyển Việt Nam hiện vẫn đứng thứ 98 thế giới với 1.228,34 điểm. Kyrgyzstan có tổng cộng 1.207,59.";

        Assert.Empty(Missing("Việt Nam có 1.228, 34 điểm, Kyrgyzstan có 1.207, 59 điểm.", article));
        Assert.Empty(Missing("Việt Nam có 1.228,34 điểm.", article));
    }

    [Fact]
    public void DateWrittenWithDotIsSpokenAsDayAndMonth()
    {
        Assert.Empty(Missing("Ngày 17 tháng 11, hội đồng quản trị lên tiếng.", "Sáng 17.11, hội đồng quản trị Nvidia ra thông báo."));
    }

    [Fact]
    public void EnglishMonthNameCountsAsItsNumber()
    {
        var reference = FactCheck.ReferenceText("Salute to Service", null, "On 21 November, NBC will air an hour-long special.", null);

        Assert.Empty(Missing("Ngày 21 tháng 11, NBC sẽ phát sóng chương trình đặc biệt.", reference));
    }

    [Fact]
    public void PublicationDateAndHeadlineNumbersCount()
    {
        var reference = FactCheck.ReferenceText("Anthropic sets IPO price at 45 dollars", null, "The company said on Monday it would list its shares.",
            new DateTime(2026, 11, 17, 3, 0, 0, DateTimeKind.Utc));

        Assert.Empty(Missing("Hôm nay 17 tháng 11, Anthropic chốt giá 45 đô la.", reference));
    }

    [Fact]
    public void InventedNumbersAreStillCaught()
    {
        var reference = FactCheck.ReferenceText("Salute to Service", null, "The NFL spends November saluting service.", null);

        Assert.Equal(new[] { "32.000" }, Missing("Có 32.000 cựu binh tham dự.", reference));
    }
}
