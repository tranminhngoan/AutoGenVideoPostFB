using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/articles")]
[Produces("application/json")]
public class ArticlesController : ControllerBase
{
    private readonly IArticleService _articleService;
    private readonly ILogger<ArticlesController> _logger;

    public ArticlesController(
        IArticleService articleService,
        ILogger<ArticlesController> logger)
    {
        _articleService = articleService;
        _logger = logger;
    }

    /// <summary>
    /// Extracts news article content and strictly internal images from a given URL.
    /// </summary>
    [HttpPost("extract")]
    [ProducesResponseType(typeof(ArticleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtractArticle([FromBody] ExtractArticleRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { error = "Article URL is required." });
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || 
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new { error = "Invalid article URL format. Must be an HTTP or HTTPS link." });
        }

        try
        {
            var article = await _articleService.ExtractAndSaveArticleAsync(request.Url, cancellationToken);
            return Ok(article);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to download article from URL {Url}", request.Url);
            return BadRequest(new { error = $"Could not download article: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error extracting article from {Url}", request.Url);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while extracting article content." });
        }
    }

    /// <summary>
    /// Gets a paginated list of extracted articles with their images.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ArticleResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetArticles([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var articles = await _articleService.GetArticlesAsync(page, pageSize, cancellationToken);
        return Ok(articles);
    }

    /// <summary>
    /// Gets an extracted article by ID including all associated images and metadata.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ArticleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetArticleById(Guid id, CancellationToken cancellationToken)
    {
        var article = await _articleService.GetArticleByIdAsync(id, cancellationToken);
        if (article == null)
        {
            return NotFound(new { error = $"Article with ID '{id}' was not found." });
        }

        return Ok(article);
    }

    /// <summary>
    /// Updates image metadata, classification category, or review status for an extracted article image.
    /// </summary>
    [HttpPut("{id:guid}/images/{imageId:guid}")]
    [ProducesResponseType(typeof(ArticleImageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateArticleImage(
        Guid id,
        Guid imageId,
        [FromBody] UpdateArticleImageDto dto,
        CancellationToken cancellationToken)
    {
        var updated = await _articleService.UpdateArticleImageAsync(id, imageId, dto, cancellationToken);
        if (updated == null)
        {
            return NotFound(new { error = $"Image with ID '{imageId}' belonging to article '{id}' was not found." });
        }

        return Ok(updated);
    }
}
