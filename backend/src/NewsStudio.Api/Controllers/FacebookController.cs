using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/facebook")]
public class FacebookController : ControllerBase
{
    private readonly IFacebookPublishingService _facebookService;

    public FacebookController(IFacebookPublishingService facebookService)
    {
        _facebookService = facebookService;
    }

    /// <summary>
    /// Lấy danh sách các Facebook Page đã kết nối (Token đã được mã hóa và mask an toàn).
    /// </summary>
    [HttpGet("pages")]
    public async Task<ActionResult<List<FacebookAccountDto>>> GetPages(CancellationToken cancellationToken)
    {
        var pages = await _facebookService.GetConnectedPagesAsync(cancellationToken);
        return Ok(pages);
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ của Page Access Token với Meta Graph API.
    /// </summary>
    [HttpPost("verify")]
    public async Task<ActionResult<VerifyTokenResultDto>> VerifyToken([FromBody] ConnectFacebookPageRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _facebookService.VerifyTokenAsync(request.PageAccessToken, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Kết nối một Facebook Page vào hệ thống (Token được mã hóa an toàn).
    /// </summary>
    [HttpPost("pages")]
    public async Task<ActionResult<FacebookAccountDto>> ConnectPage([FromBody] ConnectFacebookPageRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await _facebookService.ConnectPageAsync(request, cancellationToken);
            return Ok(account);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Ngắt kết nối một Facebook Page khỏi hệ thống.
    /// </summary>
    [HttpDelete("pages/{id:guid}")]
    public async Task<IActionResult> DisconnectPage(Guid id, CancellationToken cancellationToken)
    {
        var success = await _facebookService.DisconnectPageAsync(id, cancellationToken);
        if (!success)
        {
            return NotFound(new { message = "Không tìm thấy Facebook Page cần ngắt kết nối." });
        }
        return NoContent();
    }

    /// <summary>
    /// Xuất bản video lên Facebook Reels thông qua Meta Graph API.
    /// YÊU CẦU: Video bắt buộc phải được biên tập viên duyệt trước (VideoReview.Decision == "Approved").
    /// </summary>
    [HttpPost("/api/video-projects/{id:guid}/publish-reel")]
    public async Task<ActionResult<PublishReelResultDto>> PublishReel(
        Guid id,
        [FromBody] PublishReelRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _facebookService.PublishReelAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message, isBlocked = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Lỗi xuất bản Reels: {ex.Message}" });
        }
    }

    /// <summary>
    /// Lấy lịch sử các bài đăng Reels và số liệu của một dự án video.
    /// </summary>
    [HttpGet("/api/video-projects/{id:guid}/facebook-posts")]
    public async Task<ActionResult<List<FacebookPostDto>>> GetProjectPosts(Guid id, CancellationToken cancellationToken)
    {
        var posts = await _facebookService.GetProjectPostsAsync(id, cancellationToken);
        return Ok(posts);
    }

    /// <summary>
    /// Thu thập số liệu tương tác thực tế từ Meta Page Insights API cho một bài đăng Reels.
    /// </summary>
    [HttpGet("posts/{postId:guid}/metrics")]
    public async Task<ActionResult<VideoMetricDto>> FetchMetrics(Guid postId, CancellationToken cancellationToken)
    {
        var metric = await _facebookService.FetchMetricsAsync(postId, cancellationToken);
        if (metric == null)
        {
            return NotFound(new { message = "Không tìm thấy bản ghi bài đăng Facebook." });
        }
        return Ok(metric);
    }
}
