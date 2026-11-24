using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NewsStudio.Infrastructure.Common;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Api.Controllers;

/// <summary>
/// The app has no login: writes of the studios (AI video studio, Reup Video) need X-Studio-Key equal to the server's
/// Vercel AI Gateway key, its paid Gemini key or Security:AdminApiKey. The page asks for it once and keeps it in the browser.
/// </summary>
public static class StudioAccess
{
    /// <summary>Null when allowed; otherwise a 401 the page turns into its unlock box.</summary>
    public static IActionResult? Denied(HttpRequest request, IConfiguration configuration)
    {
        var keys = new[] { SeedanceVideoClient.ApiKey(configuration), GeminiKeys.Paid(configuration), configuration["Security:AdminApiKey"] }
            .Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()).ToList();
        if (keys.Count == 0) return null; // nothing configured to compare with
        var given = request.Headers.TryGetValue("X-Studio-Key", out var h) ? h.ToString().Trim() : "";
        if (given.Length > 0)
        {
            var g = SHA256.HashData(Encoding.UTF8.GetBytes(given));
            if (keys.Any(k => CryptographicOperations.FixedTimeEquals(g, SHA256.HashData(Encoding.UTF8.GetBytes(k))))) return null;
        }
        return new ObjectResult(new
        {
            needsStudioKey = true,
            message = given.Length == 0
                ? "Cần mở khoá Studio bằng khoá Vercel AI Gateway (AI_GATEWAY_API_KEY) hoặc khoá Gemini đang đặt trên Railway (chỉ lưu trên trình duyệt này)."
                : "Khoá không khớp với khoá Vercel / Gemini đang cấu hình trên server."
        }) { StatusCode = StatusCodes.Status401Unauthorized };
    }
}
