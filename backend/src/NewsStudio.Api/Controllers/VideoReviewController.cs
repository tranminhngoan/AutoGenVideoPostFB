using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/video-projects/{id:guid}")]
public class VideoReviewController : ControllerBase
{
    private readonly IVideoReviewService _reviewService;
    private readonly IQualityControllerService _qualityController;

    public VideoReviewController(
        IVideoReviewService reviewService,
        IQualityControllerService qualityController)
    {
        _reviewService = reviewService;
        _qualityController = qualityController;
    }

    /// <summary>
    /// Lấy báo cáo kiểm duyệt video và 10 tiêu chí chất lượng nội bộ.
    /// </summary>
    [HttpGet("review")]
    public async Task<ActionResult<VideoReviewDto>> GetReview(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var review = await _reviewService.GetProjectReviewAsync(id, cancellationToken);
            return Ok(review);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Chạy kiểm định tự động 10 tiêu chí chất lượng cho dự án video.
    /// </summary>
    [HttpPost("review/evaluate")]
    public async Task<ActionResult<VideoReviewDto>> RunEvaluation(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var review = await _reviewService.RunQualityCheckAsync(id, cancellationToken);
            return Ok(review);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Gửi quyết định kiểm duyệt (Duyệt, Từ chối, hoặc Yêu cầu làm lại).
    /// Chặn phê duyệt nếu tồn tại lỗi nghiêm trọng (Fatal Violations).
    /// </summary>
    [HttpPost("review/decision")]
    public async Task<ActionResult<VideoReviewDto>> SubmitDecision(
        Guid id,
        [FromBody] SubmitReviewDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _reviewService.SubmitDecisionAsync(id, request, cancellationToken);
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
    }

    /// <summary>
    /// Phê duyệt video sẵn sàng cho bước xuất bản (Chỉ duyệt khi không có lỗi nghiêm trọng).
    /// </summary>
    [HttpPost("review/approve")]
    public async Task<ActionResult<VideoReviewDto>> Approve(
        Guid id,
        [FromBody] SubmitReviewDecisionRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var req = request ?? new SubmitReviewDecisionRequestDto
            {
                Decision = "Approved",
                ReviewedBy = "Editor",
                FeedbackNotes = "Đạt tiêu chuẩn xuất bản nội bộ."
            };
            req.Decision = "Approved";

            var result = await _reviewService.SubmitDecisionAsync(id, req, cancellationToken);
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
    }

    /// <summary>
    /// Từ chối video kèm lý do chi tiết từ biên tập viên.
    /// </summary>
    [HttpPost("review/reject")]
    public async Task<ActionResult<VideoReviewDto>> Reject(
        Guid id,
        [FromBody] SubmitReviewDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            request.Decision = "Rejected";
            var result = await _reviewService.SubmitDecisionAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Yêu cầu thực hiện lại: Tạo lại kịch bản (RegenerateScript), Audio (RegenerateAudio), hoặc Render (ReRender).
    /// </summary>
    [HttpPost("review/request-action")]
    public async Task<IActionResult> RequestAction(
        Guid id,
        [FromBody] ReviewActionRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await _reviewService.RequestActionAsync(id, request.Action, request.Notes, cancellationToken);
            return Ok(new { success, action = request.Action });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Chỉnh sửa tiêu đề, hook và caption của video trước khi xuất bản.
    /// </summary>
    [HttpPut("review/metadata")]
    public async Task<IActionResult> UpdateMetadata(
        Guid id,
        [FromBody] UpdateVideoMetadataRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await _reviewService.UpdateMetadataAsync(id, request, cancellationToken);
            return Ok(new { success });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Xóa dự án video và dọn sạch toàn bộ file tạm thời trong bộ nhớ đệm.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteProject(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _reviewService.DeleteProjectAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(new { message = "Không tìm thấy dự án video cần xóa." });
        }
        return NoContent();
    }
}
