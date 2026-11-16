using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Trending;

namespace NewsStudio.Infrastructure.Services;

/// <summary>
/// "Tự học": what this page's audience actually watches, from the page's own Reels views — by posting hour
/// (Vietnam time), kind of story and region. Used to suggest posting times and, when the page's AutoTune is
/// on, to favour the kinds of stories that get more views when picking the next story. Only videos posted at
/// least a day ago count (their views have mostly settled), and nothing is concluded from too few videos.
/// </summary>
public class PageInsightsService : IPageInsightsService
{
    public const int MinSamples = 12;
    /// <summary>A bucket needs this many videos before it may change anything.</summary>
    public const int MinBucket = 3;

    private readonly IApplicationDbContext _context;

    public PageInsightsService(IApplicationDbContext context)
    {
        _context = context;
    }

    public sealed record Sample(DateTime PublishedAtUtc, long Views, IReadOnlyList<string> Topics, string? Region);

    public async Task<PageInsightsDto> GetAsync(Guid facebookAccountId, CancellationToken cancellationToken = default)
    {
        var settled = DateTime.UtcNow.AddDays(-1);
        var posts = await _context.FacebookPosts.AsNoTracking()
            .Where(p => p.FacebookAccountId == facebookAccountId && p.UploadStatus == "Published"
                        && p.PublishedAtUtc != null && p.PublishedAtUtc <= settled)
            .Select(p => new
            {
                p.VideoProjectId,
                PublishedAtUtc = p.PublishedAtUtc!.Value,
                Title = p.VideoProject != null ? p.VideoProject.Title : "",
                Views = p.Metrics.Where(m => m.Status == "Recorded").Select(m => (long?)m.Views).Max()
            })
            .ToListAsync(cancellationToken);

        var projectIds = posts.Select(p => (Guid?)p.VideoProjectId).ToList();
        var regions = await _context.AutoPostRuns.AsNoTracking()
            .Where(r => projectIds.Contains(r.VideoProjectId) && r.Region != null)
            .Select(r => new { r.VideoProjectId, r.Region })
            .ToListAsync(cancellationToken);
        var regionOf = regions.GroupBy(r => r.VideoProjectId!.Value).ToDictionary(g => g.Key, g => g.First().Region);

        var samples = posts
            .Where(p => p.Views != null) // never read from Facebook yet: no number, not a zero
            .Select(p => new Sample(p.PublishedAtUtc, p.Views!.Value,
                NewsTopics.Classify(p.Title, null, null).Where(t => t != NewsTopics.General).ToList(),
                regionOf.GetValueOrDefault(p.VideoProjectId)))
            .ToList();

        var settings = await _context.AutoPostSettings.AsNoTracking().FirstOrDefaultAsync(s => s.FacebookAccountId == facebookAccountId, cancellationToken);
        var result = Compute(samples, settings?.PostsPerDay ?? 5, AutoPostService.ParseSlots(settings?.TimeSlots));
        result.FacebookAccountId = facebookAccountId;
        return result;
    }

    public static PageInsightsDto Compute(IReadOnlyList<Sample> samples, int postsPerDay, IReadOnlyList<string> currentSlots)
    {
        var dto = new PageInsightsDto { Samples = samples.Count, MinSamples = MinSamples, EnoughData = samples.Count >= MinSamples };
        if (samples.Count == 0) return dto;
        var average = samples.Average(s => (double)s.Views);
        dto.AverageViews = Math.Round(average, 1);

        InsightBucketDto Bucket(string key, string label, IEnumerable<Sample> group)
        {
            var list = group.ToList();
            var avg = list.Average(s => (double)s.Views);
            return new InsightBucketDto { Key = key, Label = label, Videos = list.Count, AverageViews = Math.Round(avg, 1), Ratio = average > 0 ? Math.Round(avg / average, 2) : 1 };
        }

        dto.Hours = samples
            .GroupBy(s => (s.PublishedAtUtc + AutoPostService.VietnamOffset).Hour)
            .OrderBy(g => g.Key)
            .Select(g => Bucket(g.Key.ToString(CultureInfo.InvariantCulture), $"{g.Key:00}:00", g))
            .ToList();
        dto.Topics = samples
            .SelectMany(s => s.Topics.Select(t => (Topic: t, Sample: s)))
            .GroupBy(x => x.Topic)
            .Select(g => Bucket(g.Key, NewsTopics.NameOf(g.Key), g.Select(x => x.Sample)))
            .OrderByDescending(b => b.Videos >= MinBucket).ThenByDescending(b => b.AverageViews)
            .ToList();
        dto.Regions = samples
            .Where(s => s.Region is "VN" or "World")
            .GroupBy(s => s.Region!)
            .Select(g => Bucket(g.Key, g.Key == "VN" ? "Việt Nam" : "Quốc tế", g))
            .OrderByDescending(b => b.AverageViews)
            .ToList();

        if (dto.EnoughData) dto.SuggestedSlots = SuggestSlots(dto.Hours, postsPerDay, currentSlots);
        return dto;
    }

    /// <summary>
    /// Best hours (06:00–23:00, at least two videos each), one slot per hour; the rest of the day keeps the
    /// current times so the number of posts stays the same.
    /// </summary>
    public static List<string> SuggestSlots(IReadOnlyList<InsightBucketDto> hours, int postsPerDay, IReadOnlyList<string> currentSlots)
    {
        var count = Math.Max(1, postsPerDay);
        var best = hours
            .Where(h => h.Videos >= 2 && int.TryParse(h.Key, out var hr) && hr >= 6 && hr <= 23)
            .OrderByDescending(h => h.AverageViews)
            .Select(h => int.Parse(h.Key, CultureInfo.InvariantCulture))
            .Take(count)
            .ToList();

        var slots = best.Select(hr =>
                currentSlots.FirstOrDefault(s => s.StartsWith($"{hr:00}:", StringComparison.Ordinal)) ?? $"{hr:00}:00")
            .ToList();
        foreach (var s in currentSlots)
        {
            if (slots.Count >= count) break;
            var hr = s[..2];
            if (!slots.Any(x => x.StartsWith(hr, StringComparison.Ordinal))) slots.Add(s);
        }
        return slots.OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Weight for a candidate story (multiplies its hot score): kinds of stories and regions that get more
    /// views on this page come first. 1 when there isn't enough data; kept between 0.65 and 1.7.
    /// </summary>
    public static double Boost(PageInsightsDto insights, IEnumerable<string> topics, string? region)
    {
        if (!insights.EnoughData) return 1;
        var topicRatios = insights.Topics.Where(b => b.Videos >= MinBucket && topics.Contains(b.Key)).Select(b => b.Ratio).ToList();
        var topic = topicRatios.Count > 0 ? Math.Clamp(topicRatios.Max(), 0.75, 1.4) : 1;
        var regionBucket = insights.Regions.FirstOrDefault(b => b.Key == region && b.Videos >= MinBucket);
        var reg = regionBucket != null ? Math.Clamp(regionBucket.Ratio, 0.85, 1.2) : 1;
        return Math.Round(topic * reg, 3);
    }
}
