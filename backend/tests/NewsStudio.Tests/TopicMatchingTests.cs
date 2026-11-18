using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.Ai;
using NewsStudio.Infrastructure.Trending;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>Stories that reached the wrong page on production.</summary>
public class TopicMatchingTests
{
    [Theory]
    [InlineData("EU giải ngân thêm hơn 1,7 tỷ Euro cho Ukraine", "Khoản giải ngân nằm trong gói cho vay trị giá 90 tỷ Euro dành cho Ukraine.")]
    [InlineData("Italy phạt Meta 805 triệu Euro", null)]
    [InlineData("Xuất hiện siêu tàu \"Việt Nam Thịnh Vượng\" dài hơn 3 sân bóng đá, cao bằng tòa nhà 20 tầng", "Con tàu chở dầu thô dài tới 333 m.")]
    [InlineData("Nhân viên khách sạn tranh tài tại cuộc thi “Olympic dọn phòng”", null)]
    [InlineData("Chủ tịch Hà Nội: Không sử dụng đất lúa trái quy định để làm sân golf", null)]
    public void NotSport(string title, string? summary)
    {
        Assert.DoesNotContain("the-thao", NewsTopics.Classify(title, summary, null));
    }

    [Theory]
    [InlineData("Tuyển Việt Nam chốt danh sách dự VCK EURO", null)]
    [InlineData("Lịch thi đấu Euro 2028 mới nhất", null)]
    [InlineData("Haaland bùng nổ giúp Na Uy giành vé đi tiếp ở phút 90+6", "Bàn thắng quyết định của tiền đạo Man City.")]
    [InlineData("Thái Lan công bố xây sân 100.000 chỗ, tham vọng đăng cai World Cup", null)]
    [InlineData("Đoàn thể thao Việt Nam chuẩn bị cho Olympic 2028", null)]
    public void StillSport(string title, string? summary)
    {
        Assert.Contains("the-thao", NewsTopics.Classify(title, summary, null));
    }

    [Fact]
    public void CapitalKeywordDoesNotMatchTheVietnameseWord()
    {
        var keywords = new List<string> { "AI" };

        Assert.False(NewsTopics.MatchesKeywords("Ai có thể thuộc diện bị tạm hoãn xuất cảnh vì nợ thuế?", null, keywords));
        Assert.True(NewsTopics.MatchesKeywords("Google ra mắt mô hình AI mới", null, keywords));
    }

    [Fact]
    public void AccentedKeywordMatchesOnlyWithItsAccents()
    {
        var keywords = new List<string> { "vàng" };

        Assert.True(NewsTopics.MatchesKeywords("Giá Vàng miếng hôm nay tăng mạnh", null, keywords));
        Assert.False(NewsTopics.MatchesKeywords("Đường phố vắng tanh ngày mưa bão", null, keywords));
    }

    [Fact]
    public void KeywordWithoutAccentsStillMatchesBothWays()
    {
        var keywords = new List<string> { "bitcoin", "chien tranh" };

        Assert.True(NewsTopics.MatchesKeywords("Bitcoin lập đỉnh mới", null, keywords));
        Assert.True(NewsTopics.MatchesKeywords("Nguy cơ chiến tranh thương mại", null, keywords));
    }

    [Fact]
    public void OffTopicStoryNeedsAnotherStory()
    {
        var pass = EditorialAssistantService.Parse(
            "{\"consistent\": true, \"issues\": [], \"caption\": \"Tin chiến sự\", \"hashtags\": [], \"first_comment\": \"?\", " +
            "\"on_topic\": false, \"off_topic_reason\": \"Tin quân sự, không phải thể thao.\"}",
            "Nguồn: VietNamNet", "https://example.com/a");

        Assert.False(pass.OnTopic);
        var gate = QualityGate.Evaluate(new FactSheetDto(), pass, 2000);
        Assert.Equal(QualityGateStatus.ReviewRequired, gate.Status);
        Assert.Contains(gate.Reasons, r => r.Contains("không đúng chủ đề"));
    }

    [Fact]
    public void MissingOnTopicFieldMeansOnTopic()
    {
        var pass = EditorialAssistantService.Parse(
            "{\"consistent\": true, \"issues\": [], \"caption\": \"Tin bóng đá\", \"hashtags\": [], \"first_comment\": \"?\"}",
            null, "https://example.com/a");

        Assert.True(pass.OnTopic);
    }
}
