using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Infrastructure.Rendering;

namespace NewsStudio.Api.Controllers;

/// <summary>Brand kit of a page: its logo, shown in the brand pill of videos and digests.</summary>
[ApiController]
[Route("api/auto-post/pages/{facebookAccountId:guid}/logo")]
public class BrandKitController : ControllerBase
{
    /// <summary>Logos are tiny (shown at 44 px): kept in the database, no file storage needed.</summary>
    public const int MaxLogoBytes = 400 * 1024;

    private readonly IApplicationDbContext _context;

    public BrandKitController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid facebookAccountId, CancellationToken cancellationToken)
    {
        var logo = await _context.AutoPostSettings.AsNoTracking()
            .Where(s => s.FacebookAccountId == facebookAccountId).Select(s => s.BrandLogo).FirstOrDefaultAsync(cancellationToken);
        if (logo is not { Length: > 0 }) return NotFound();
        var type = SceneImageService.DetectImageType(logo) switch { "png" => "image/png", "webp" => "image/webp", _ => "image/jpeg" };
        return File(logo, type);
    }

    [HttpPut]
    [RequestSizeLimit(MaxLogoBytes + 64 * 1024)]
    public async Task<IActionResult> Upload(Guid facebookAccountId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Chưa chọn file logo." });
        if (file.Length > MaxLogoBytes) return BadRequest(new { message = "Logo quá lớn (tối đa 400 KB). Dùng ảnh vuông khoảng 256×256 px." });
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (SceneImageService.DetectImageType(bytes) == null) return BadRequest(new { message = "Logo phải là ảnh PNG, JPEG hoặc WebP." });

        var settings = await _context.AutoPostSettings.FirstOrDefaultAsync(s => s.FacebookAccountId == facebookAccountId, cancellationToken);
        if (settings == null) return NotFound(new { message = "Page này chưa được kết nối." });
        settings.BrandLogo = bytes;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(Guid facebookAccountId, CancellationToken cancellationToken)
    {
        var settings = await _context.AutoPostSettings.FirstOrDefaultAsync(s => s.FacebookAccountId == facebookAccountId, cancellationToken);
        if (settings == null) return NotFound();
        settings.BrandLogo = null;
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
