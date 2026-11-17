using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Ai;
using NewsStudio.Infrastructure.Common;
using NewsStudio.Infrastructure.Rendering;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>
/// Acceptance scenarios of the newsroom upgrade (the parts that are decided by code, without network or AI):
/// identity of people, pictures of events, charts from the article's numbers, unverified content → review,
/// licences, the publication gate and the AI cost meter.
/// </summary>
public class NewsroomAcceptanceTests
{
    private const string Article =
        "Chiều 16/11, TAND TP Hà Nội tuyên phạt Phó Đức Nam (Mr Pips) tù chung thân về tội lừa đảo, buộc bồi thường 822 tỷ đồng. " +
        "Giá vàng miếng SJC sáng nay ở mức 152,8 triệu đồng/lượng, tăng từ 148,5 triệu đồng ngày 12/11, 149,2 triệu ngày 13/11 và 151,3 triệu ngày 15/11. " +
        "Theo nguồn tin chưa được xác nhận, công ty có thể bị điều tra thêm.";

    // ---------- TEST 1: a real person is never shown with someone else's picture

    [Fact]
    public void T1_APersonIsMatchedByTheirWikidataItemNotByAnyResult()
    {
        var results = new[]
        {
            ("Q1", "Phó Đức Nam Store", "label", "Phó Đức Nam Store"),
            ("Q2", "Lionel Messi", "alias", "Messi")
        };
        CommonsImageFinder.BestMatch("Messi", results)!.Value.Id.Should().Be("Q2", "an exact alias identifies the person");
        CommonsImageFinder.BestMatch("Nam", new[] { ("Q3", "Nam Định", "label", "Nam Định") }).Should().BeNull("one word that isn't the label is not an identity");
        CommonsImageFinder.BestMatch("Phó Đức Nam", new[] { ("Q4", "Phó Đức Nam", "label", "Phó Đức Nam") })!.Value.Confidence.Should().Be(0.95);
    }

    [Fact]
    public void T1_APersonsPictureMustBeVerifiedOrTheVideoGoesToReview()
    {
        var project = ProjectWithImage(new ArticleImage { SubjectName = "Phó Đức Nam", SubjectType = "A", VerificationStatus = "UNVERIFIED" });
        ContentGateService.PictureProblems(project, Sheet(("Phó Đức Nam", "A"))).Should().ContainSingle().Which.Should().Contain("chưa xác minh");

        var verified = ProjectWithImage(new ArticleImage { SubjectName = "Phó Đức Nam", SubjectType = "A", VerificationStatus = "VERIFIED", ImageType = ImageKinds.Real });
        ContentGateService.PictureProblems(verified, Sheet(("Phó Đức Nam", "A"))).Should().BeEmpty();
    }

    [Fact]
    public void T1_AnArticlePhotoIsAboutThePersonWhenThePublishersCaptionNamesThem()
    {
        var img = new ArticleImage { Caption = "Bị cáo Phó Đức Nam tại toà chiều 16/11. Ảnh: Giang Huy" };
        SceneImageService.TagArticleImage(img, new Article { Publisher = "VnExpress", Url = "https://vnexpress.net/a" }, Sheet(("Phó Đức Nam", "A")).Entities);
        img.SubjectType.Should().Be("A");
        img.VerificationStatus.Should().Be("VERIFIED");
        img.SourceName.Should().Be("VnExpress");
        img.ImageType.Should().Be(ImageKinds.Real);

        // Several named people: no single name tag (it could point at the wrong person)
        var group = new ArticleImage { Caption = "Phó Đức Nam và Lê Khắc Ngọ tại toà" };
        SceneImageService.TagArticleImage(group, new Article { Publisher = "VnExpress" }, Sheet(("Phó Đức Nam", "A"), ("Lê Khắc Ngọ", "A")).Entities);
        group.SubjectType.Should().Be("A+");
        var project = ProjectWithImage(group);
        project.Scenes.First().SelectedArticleImage!.SubjectType.Should().NotBe("A", "the video only tags pictures of exactly one person");

        var other = new ArticleImage { Caption = "Toàn cảnh phiên toà" };
        SceneImageService.TagArticleImage(other, new Article { Publisher = "VnExpress" }, Sheet(("Phó Đức Nam", "A")).Entities);
        other.SubjectName.Should().BeNull("nothing says who is in this photo");
        VideoProductionService.ImageCredit(other, "VnExpress").Should().Be("Ảnh: VnExpress");
    }

    // ---------- TEST 5: no picture of another incident

    [Fact]
    public void T5_StoriesAboutAPersonOrAnEventNeverUseKeywordIllustrations()
    {
        SceneImageService.AllowGenericIllustrations(Sheet(("Tai nạn xe container trên quốc lộ 51", "D")).Entities).Should().BeFalse();
        SceneImageService.AllowGenericIllustrations(Sheet(("Phó Đức Nam", "A")).Entities).Should().BeFalse();
        SceneImageService.AllowGenericIllustrations(Sheet(("Chip Blackwell", "B"), ("Giá vàng", "E")).Entities).Should().BeTrue();
    }

    // ---------- TEST 4: charts only from the article's numbers

    [Fact]
    public void T4_AChartIsKeptOnlyWhenEveryValueIsInTheArticle()
    {
        var numbers = FactCheck.NumberVariants(Article);
        var good = new FactChartDto { Points = { P("12/11", 148.5), P("13/11", 149.2), P("15/11", 151.3), P("16/11", 152.8) } };
        FactCheck.ChartIsFromArticle(good, numbers).Should().BeTrue();

        var invented = new FactChartDto { Points = { P("12/11", 148.5), P("13/11", 149.2), P("14/11", 150.1) } };
        FactCheck.ChartIsFromArticle(invented, numbers).Should().BeFalse("150,1 is not in the article");
    }

    // ---------- TEST 6: unverified content → review

    [Fact]
    public void T6_NumbersThatAreNotInTheArticleSendTheVideoToReview()
    {
        var sheet = FactCheck.Check(new FactSheetDto(), "Phó Đức Nam bị phạt tù chung thân, bồi thường 900 tỷ đồng.", Article);
        sheet.NumbersNotInArticle.Should().Equal("900");

        var gate = QualityGate.Evaluate(sheet, new EditorialPassDto { Consistent = true }, Article.Length);
        gate.Status.Should().Be(QualityGateStatus.ReviewRequired);
        gate.Reasons.Should().Contain(r => r.Contains("900"));
    }

    [Fact]
    public void T6_ClaimsWithoutASourceSentenceSendTheVideoToReview()
    {
        var sheet = new FactSheetDto
        {
            Claims =
            {
                new FactClaimDto { Fact = "Phạt tù chung thân", Quote = "TAND TP Hà Nội tuyên phạt Phó Đức Nam (Mr Pips) tù chung thân về tội lừa đảo" },
                new FactClaimDto { Fact = "Bị bắt ở sân bay", Quote = "Nam bị bắt tại sân bay Nội Bài khi đang bỏ trốn" },
                new FactClaimDto { Fact = "Tài sản bị phong toả", Quote = "Toàn bộ tài sản đã bị phong toả từ năm ngoái" }
            }
        };
        FactCheck.Check(sheet, "Phó Đức Nam bị phạt tù chung thân.", Article);
        sheet.Claims[0].QuoteFound.Should().BeTrue();
        sheet.Claims[1].QuoteFound.Should().BeFalse();
        QualityGate.Evaluate(sheet, new EditorialPassDto { Consistent = true }, Article.Length).Status.Should().Be(QualityGateStatus.ReviewRequired);
    }

    [Fact]
    public void T6_ACheckedStoryIsApprovedAndAContradictionIsRejected()
    {
        var sheet = FactCheck.Check(new FactSheetDto
        {
            Claims = { new FactClaimDto { Fact = "Bồi thường 822 tỷ", Quote = "buộc bồi thường 822 tỷ đồng" } }
        }, "Phó Đức Nam bị phạt tù chung thân và phải bồi thường 822 tỷ đồng.", Article);
        QualityGate.Evaluate(sheet, new EditorialPassDto { Consistent = true }, Article.Length).Status.Should().Be(QualityGateStatus.Approved);

        var rejected = QualityGate.Evaluate(sheet, new EditorialPassDto { Consistent = false, Issues = { "Video ghi án tử hình, bài gốc ghi chung thân" } }, Article.Length);
        rejected.Status.Should().Be(QualityGateStatus.Rejected);
    }

    // ---------- TEST 7: unclear licence → not used

    [Fact]
    public void T7_OnlyFreeLicencesAreTakenFromCommons()
    {
        CommonsImageFinder.IsFreeLicense("CC BY-SA 4.0").Should().BeTrue();
        CommonsImageFinder.IsFreeLicense("Public domain").Should().BeTrue();
        CommonsImageFinder.IsFreeLicense("Fair use").Should().BeFalse();
        CommonsImageFinder.IsFreeLicense("All rights reserved").Should().BeFalse();
    }

    // ---------- Fact sheet comes with the script (no extra AI call)

    [Fact]
    public void TheFactSheetIsReadFromTheScriptAnswer()
    {
        using var doc = JsonDocument.Parse(
            "{\"entities\": [{\"name\": \"Phó Đức Nam\", \"category\": \"a\", \"kind\": \"người\"}, {\"name\": \"x\", \"category\": \"Z\"}]," +
            " \"claims\": [{\"fact\": \"Tù chung thân\", \"quote\": \"tù chung thân\", \"certainty\": \"unconfirmed\"}]," +
            " \"imageQueries\": [\"courtroom\", \"gavel judge\"]," +
            " \"chart\": {\"title\": \"Giá vàng\", \"points\": [{\"label\": \"12/11\", \"value\": \"148,5\"}]}}");
        var sheet = FactCheck.ParseFactSheet(doc.RootElement);
        sheet.Entities.Select(e => e.Category).Should().Equal("A", "D");
        sheet.Claims.Single().Certainty.Should().Be("unconfirmed");
        sheet.ImageQueries.Should().Equal("courtroom", "gavel judge");
        sheet.Chart!.Points.Single().Value.Should().Be(148.5);
    }

    // ---------- AI cost

    [Fact]
    public void FreeKeyCallsCostNothingAndTheMonthStartsInVietnamTime()
    {
        var meter = new AiUsageMeter(null!, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), null!);
        meter.Cost(speech: true, input: 1000, output: 2_000_000, free: true).Should().Be(0);
        meter.Cost(speech: true, input: 0, output: 1_000_000, free: false).Should().Be(10m);
        meter.Cost(speech: false, input: 1_000_000, output: 0, free: false).Should().Be(0.30m);
        AiUsageMeter.MonthStartUtc(new DateTime(2026, 11, 30, 18, 0, 0, DateTimeKind.Utc)).Should().Be(new DateTime(2026, 11, 30, 17, 0, 0, DateTimeKind.Utc),
            "01:00 on 1 December in Vietnam is already the new month");
        AiUsageMeter.Normalize(new AiBudgetSettingsDto { MonthlyUsd = 20, Mode = "boom" }).Mode.Should().Be(AiUsageMeter.Warn);
    }

    // ---------- helpers

    private static FactChartPointDto P(string label, double value) => new() { Label = label, Value = value };

    private static FactSheetDto Sheet(params (string Name, string Category)[] entities) =>
        new() { Entities = entities.Select(e => new FactEntityDto { Name = e.Name, Category = e.Category }).ToList() };

    private static VideoProject ProjectWithImage(ArticleImage image)
    {
        image.Id = Guid.NewGuid();
        var project = new VideoProject { Title = "t", Publisher = "VnExpress" };
        project.Scenes.Add(new VideoScene { SceneNumber = 1, SelectedArticleImage = image, SelectedArticleImageId = image.Id });
        return project;
    }
}
