using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

/// <summary>First comments with a question, and viewers' comments with AI-drafted replies the owner sends.</summary>
public interface IEngagementService
{
    Task RunAsync(CancellationToken ct);
    Task<bool> FirstCommentEnabledAsync(CancellationToken ct);
    Task SetFirstCommentEnabledAsync(bool enabled, CancellationToken ct);
    Task<List<PostCommentDto>> ListCommentsAsync(string status, CancellationToken ct);
    Task<PostCommentDto> ReplyAsync(Guid commentId, string message, CancellationToken ct);
    Task DismissAsync(Guid commentId, CancellationToken ct);
}
