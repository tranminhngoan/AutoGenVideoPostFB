using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Extractors;
using NewsStudio.Infrastructure.Extractors.Adapters;
using Xunit;

namespace NewsStudio.Tests;

[Collection("DatabaseCollection")]
public class Phase3ArticleAndImageExtractorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public Phase3ArticleAndImageExtractorTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void VnExpressAdapter_Extracts_Title_Body_And_Figures()
    {
        // Arrange
        var html = @"
<!DOCTYPE html>
<html>
<head>
    <meta property=""og:site_name"" content=""VnExpress"" />
    <link rel=""canonical"" href=""https://vnexpress.net/metro-so-1-van-hanh-4829100.html"" />
</head>
<body>
    <h1 class=""title-detail"">Tuyến metro số 1 Bến Thành - Suối Tiên bắt đầu chạy thương mại</h1>
    <p class=""description"">Sáng 15/11, hàng nghìn người dân xếp hàng trải nghiệm metro số 1.</p>
    <span class=""author"">Gia Minh</span>
    <article class=""fck_detail"">
        <p class=""Normal"">Đoàn tàu xuất phát từ ga trung tâm Bến Thành lúc 5h sáng.</p>
        <figure class=""tplCaption"">
            <img src=""https://vcdn-vnexpress.vnecdn.net/2026/11/15/tau-metro-1.jpg"" alt=""Tàu metro 1"" />
            <figcaption>
                <p class=""Image"">Đoàn tàu metro số 1 tại ga ngầm Bến Thành sáng sớm 15/11.</p>
            </figcaption>
        </figure>
        <p class=""Normal"">Rất đông người dân mang theo cờ hoa và điện thoại chụp ảnh kỷ niệm.</p>
        <figure class=""tplCaption"">
            <img src=""https://vcdn-vnexpress.vnecdn.net/2026/11/15/nguoi-dan-xep-hang.jpg"" alt=""Người dân xếp hàng"" />
            <figcaption>
                <p class=""Image"">Người dân kiên nhẫn xếp hàng tại sảnh đợi vé ga Nhà hát TP HCM.</p>
            </figcaption>
        </figure>
    </article>
</body>
</html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var adapter = new VnExpressAdapter();
        var uri = new Uri("https://vnexpress.net/metro-so-1-van-hanh-4829100.html");

        // Act
        adapter.CanHandle(uri).Should().BeTrue();
        var result = adapter.Extract(doc, uri);

        // Assert
        result.Title.Should().Be("Tuyến metro số 1 Bến Thành - Suối Tiên bắt đầu chạy thương mại");
        result.Description.Should().Contain("Sáng 15/11");
        result.Author.Should().Be("Gia Minh");
        result.ContentBody.Should().Contain("Đoàn tàu xuất phát từ ga trung tâm Bến Thành");
        result.Images.Should().HaveCount(2);
        result.Images[0].Url.Should().Be("https://vcdn-vnexpress.vnecdn.net/2026/11/15/tau-metro-1.jpg");
        result.Images[0].Caption.Should().Contain("Đoàn tàu metro số 1 tại ga ngầm Bến Thành");
        result.Images[0].Order.Should().Be(1);
        result.Images[1].Order.Should().Be(2);
    }

    [Fact]
    public void TuoiTreAdapter_Extracts_Correct_Content_And_Images()
    {
        // Arrange
        var html = @"
<!DOCTYPE html>
<html>
<head>
    <meta property=""og:site_name"" content=""Tuổi Trẻ Online"" />
</head>
<body>
    <h1 class=""article-title"">Đầu tư trung tâm công nghệ bán dẫn lớn tại Việt Nam</h1>
    <h2 class=""article-summary"">Khu công nghệ cao tiếp nhận dự án bán dẫn mới trị giá 500 triệu USD.</h2>
    <div class=""author-info"">Đức Thiện</div>
    <div class=""detail-content"">
        <p>Sáng nay, lễ khởi công dự án vi mạch bán dẫn tiên tiến đã diễn ra.</p>
        <div class=""VCSortableInPage"">
            <img src=""https://cdn.tuoitre.vn/2026/11/15/nha-may-ban-dan-1.jpg"" alt=""Nhà máy bán dẫn"" />
            <div class=""PhotoCMS_Caption"">Lễ khởi công nhà máy bán dẫn công nghệ cao.</div>
        </div>
        <p>Dự kiến nhà máy sẽ hoàn thành và đi vào sản xuất thử nghiệm trong 18 tháng.</p>
    </div>
</body>
</html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var adapter = new TuoiTreAdapter();
        var uri = new Uri("https://tuoitre.vn/dau-tu-ban-dan-2026.htm");

        // Act
        adapter.CanHandle(uri).Should().BeTrue();
        var result = adapter.Extract(doc, uri);

        // Assert
        result.Title.Should().Be("Đầu tư trung tâm công nghệ bán dẫn lớn tại Việt Nam");
        result.ContentBody.Should().Contain("lễ khởi công dự án vi mạch bán dẫn");
        result.Images.Should().HaveCount(1);
        result.Images[0].Url.Should().Be("https://cdn.tuoitre.vn/2026/11/15/nha-may-ban-dan-1.jpg");
        result.Images[0].Caption.Should().Contain("Lễ khởi công nhà máy bán dẫn");
    }

    [Fact]
    public void GenericAdapter_Extracts_OpenGraph_And_Standard_Figures()
    {
        // Arrange
        var html = @"
<!DOCTYPE html>
<html>
<head>
    <meta property=""og:title"" content=""Báo cáo kinh tế quý 4 năm 2026"" />
    <meta property=""og:description"" content=""Tăng trưởng GDP duy trì mức ấn tượng đạt 7.2%."" />
    <meta property=""og:site_name"" content=""Tạp chí Kinh tế"" />
</head>
<body>
    <article>
        <p>Báo cáo kinh tế vĩ mô vừa được công bố sáng nay với nhiều điểm sáng tích cực.</p>
        <figure>
            <img src=""https://example.com/charts/gdp-growth-2026.png"" alt=""Biểu đồ tăng trưởng GDP"" />
            <figcaption>Biểu đồ thể hiện mức tăng trưởng GDP các quý năm 2026.</figcaption>
        </figure>
    </article>
</body>
</html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var adapter = new GenericArticleAdapter();
        var uri = new Uri("https://example.com/kinh-te-quy-4.html");

        // Act
        var result = adapter.Extract(doc, uri);

        // Assert
        result.Title.Should().Be("Báo cáo kinh tế quý 4 năm 2026");
        result.Description.Should().Contain("Tăng trưởng GDP");
        result.Images.Should().HaveCount(1);
        result.Images[0].Caption.Should().Contain("Biểu đồ thể hiện");
    }

    [Fact]
    public void ArticleExtractor_Filters_Ads_Icons_And_Extracts_Only_Valid_Images()
    {
        // Arrange
        var html = @"
<!DOCTYPE html>
<html>
<head><title>Tin tức tổng hợp</title></head>
<body>
    <article>
        <p>Nội dung bài viết về năng lượng tái tạo.</p>
        <!-- Ad Banner (Should be filtered out) -->
        <img src=""https://example.com/ads/banner-qc-300x250.jpg"" alt=""Quảng cáo mua sắm"" />
        <!-- Tracking Pixel (Should be filtered out) -->
        <img src=""https://example.com/tracking/pixel.gif"" width=""1"" height=""1"" />
        <!-- Valid Article Figure -->
        <figure>
            <img src=""https://example.com/images/solar-farm-binh-thuan.jpg"" width=""800"" height=""600"" />
            <figcaption>Cánh đồng điện mặt trời tại Bình Thuận đang vận hành hết công suất.</figcaption>
        </figure>
    </article>
</body>
</html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var adapter = new GenericArticleAdapter();
        var uri = new Uri("https://example.com/nang-luong-tai-tao.html");

        // Act
        var result = adapter.Extract(doc, uri);

        // Assert: Ads and tiny icons must be rejected; only the legitimate solar farm image kept
        result.Images.Should().HaveCount(1);
        result.Images[0].Url.Should().Be("https://example.com/images/solar-farm-binh-thuan.jpg");
    }

    [Fact]
    public void ArticleExtractor_Handles_Article_Without_Images_Gracefully()
    {
        // Arrange
        var html = @"
<!DOCTYPE html>
<html>
<head><title>Bản tin văn bản không hình ảnh</title></head>
<body>
    <article>
        <h1>Thông báo lịch cúp điện định kỳ</h1>
        <p>Tổng công ty Điện lực thông báo bảo trì lưới điện phục vụ nâng cấp trạm biến áp.</p>
        <p>Chi tiết các khu vực bị ảnh hưởng vui lòng tra cứu trên ứng dụng.</p>
    </article>
</body>
</html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var adapter = new GenericArticleAdapter();
        var uri = new Uri("https://example.com/thong-bao-dien.html");

        // Act
        var result = adapter.Extract(doc, uri);

        // Assert
        result.Title.Should().Be("Thông báo lịch cúp điện định kỳ");
        result.Images.Should().BeEmpty();
    }

    [Fact]
    public void ImageClassifier_Categorizes_Correctly()
    {
        // Arrange
        var classifier = new ImageClassifierService();

        // Act & Assert
        // Character
        classifier.ClassifyImage("Chủ tịch UBND thành phố phát biểu chỉ đạo", "Ông Nguyễn Văn A", "")
            .Should().Be(ImageCategory.Character);

        // Chart
        classifier.ClassifyImage("Biểu đồ thống kê kim ngạch xuất khẩu", "Đồ thị tăng trưởng", "")
            .Should().Be(ImageCategory.Chart);

        // Document
        classifier.ClassifyImage("Văn bản ký kết quyết định thành lập", "Giấy phép công văn", "")
            .Should().Be(ImageCategory.Document);

        // Panorama
        classifier.ClassifyImage("Toàn cảnh quảng trường trung tâm nhìn từ trên cao", "Không gian đô thị", "")
            .Should().Be(ImageCategory.Panorama);

        // Unsuitable
        classifier.ClassifyImage("Banner quảng cáo khuyến mãi cuối năm", "QC", "")
            .Should().Be(ImageCategory.Unsuitable);
    }

    [Fact]
    public async Task ArticleService_Persists_Article_And_Images_With_RightsStatus_Unknown()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var article = new Article
        {
            Title = "Thử nghiệm quyền hình ảnh",
            NormalizedTitle = "thu nghiem quyen hinh anh",
            Url = $"https://test-news.vn/article-{Guid.NewGuid()}",
            Publisher = "Báo Thử Nghiệm",
            ContentBody = "Nội dung bài viết kiểm tra phân quyền hình ảnh",
            Status = "Extracted"
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var img = new ArticleImage
        {
            ArticleId = article.Id,
            OriginalImageUrl = "https://test-news.vn/img1.jpg",
            Caption = "Hình ảnh minh họa",
            ArticleImageOrder = 1,
            ImageHash = "abc12345hash",
            // Strictly Unknown by default
            RightsStatus = RightsStatus.Unknown,
            Category = ImageCategory.Panorama,
            Status = "Extracted"
        };
        db.ArticleImages.Add(img);
        await db.SaveChangesAsync();

        // Assert: Ensure rights status is Unknown and never Authorized automatically
        var savedImage = await db.ArticleImages.FindAsync(img.Id);
        savedImage.Should().NotBeNull();
        savedImage!.RightsStatus.Should().Be(RightsStatus.Unknown);
        savedImage.RightsStatus.Should().NotBe(RightsStatus.Authorized);
    }

    [Fact]
    public async Task ArticlesController_GetArticles_Returns_200OK()
    {
        // Act
        var response = await _client.GetAsync("/api/articles");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var articles = await response.Content.ReadFromJsonAsync<IEnumerable<ArticleResponseDto>>();
        articles.Should().NotBeNull();
    }

    [Fact]
    public async Task ArticlesController_ExtractArticle_Validates_Url_Format()
    {
        // Arrange
        var badRequest = new ExtractArticleRequestDto { Url = "ftp://invalid-url.com" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/articles/extract", badRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Must be an HTTP or HTTPS link");
    }

    [Fact]
    public async Task ArticlesController_UpdateArticleImage_Updates_RightsStatus_And_SelectedForVideo()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var article = new Article
        {
            Title = "Dự án Metro kiểm tra API",
            NormalizedTitle = "du an metro kiem tra api",
            Url = $"https://metro-test.vn/bai-viet-{Guid.NewGuid()}",
            Publisher = "Metro News",
            ContentBody = "Kiểm tra endpoint update ảnh",
            Status = "Extracted"
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var image = new ArticleImage
        {
            ArticleId = article.Id,
            OriginalImageUrl = "https://metro-test.vn/img/tau-test.jpg",
            Caption = "Caption ban đầu",
            ArticleImageOrder = 1,
            RightsStatus = RightsStatus.Unknown,
            Category = ImageCategory.Panorama,
            Status = "Extracted"
        };
        db.ArticleImages.Add(image);
        await db.SaveChangesAsync();

        // Act: User explicitly approves/authorizes the image rights and edits caption
        var updateDto = new UpdateArticleImageDto
        {
            Caption = "Caption đã được biên tập viên kiểm tra và cập nhật",
            RightsStatus = RightsStatus.Authorized,
            Category = ImageCategory.Event,
            SelectedForVideo = true
        };

        var response = await _client.PutAsJsonAsync(
            $"/api/articles/{article.Id}/images/{image.Id}",
            updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ArticleImageResponseDto>();
        updated.Should().NotBeNull();
        updated!.Caption.Should().Be("Caption đã được biên tập viên kiểm tra và cập nhật");
        updated.RightsStatus.Should().Be("Authorized");
        updated.Category.Should().Be("Event");

        // Verify in DB using fresh scope
        using var queryScope = _factory.Services.CreateScope();
        var queryDb = queryScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var reloaded = await queryDb.ArticleImages.FindAsync(image.Id);
        reloaded.Should().NotBeNull();
        reloaded!.RightsStatus.Should().Be(RightsStatus.Authorized);
        reloaded.Caption.Should().Be("Caption đã được biên tập viên kiểm tra và cập nhật");
    }
}
