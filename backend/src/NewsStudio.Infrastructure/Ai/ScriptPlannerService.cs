using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Ai;

public class ScriptPlannerService : IScriptPlannerService
{
    public Task<List<VideoScenePlanDto>> PlanScenesAsync(
        ScriptPlanningInput input,
        CancellationToken cancellationToken = default)
    {
        var script = input.Script;
        // Scenes cover the real narration. Its length follows the article (estimated from the word
        // count here; re-fitted to the measured audio length when the video is rendered).
        var spokenWords = CountWords(script.VoiceOverScript);
        var targetDuration = spokenWords > 0
            ? Math.Max(5.0, Math.Round(spokenWords / PromptTemplates.WordsPerSecond, 1))
            : (input.TargetDurationSeconds > 0 ? input.TargetDurationSeconds : 60);

        // Filter valid article images: strictly from current article only, not rejected, not unsuitable
        var validImages = input.AvailableImages
            .Where(img => img.RightsStatus != "Rejected" && img.Category != "Unsuitable" && img.SelectedForVideo)
            .OrderBy(img => img.Order)
            .ToList();

        // One scene per ~7 seconds of narration, built only from sentences that are actually spoken
        var segments = BuildNarrativeSegments(script, targetDuration);
        var sceneCount = segments.Count;

        // Allocate duration across scenes proportional to speech length
        var totalWords = segments.Sum(s => CountWords(s.VoiceText));
        if (totalWords <= 0) totalWords = 1;

        var scenes = new List<VideoScenePlanDto>();
        double currentStart = 0;

        for (int i = 0; i < sceneCount; i++)
        {
            var segment = segments[i];
            var segmentWords = CountWords(segment.VoiceText);

            // Scene duration proportional to the words it narrates; the last scene ends with the narration
            double duration = i == sceneCount - 1
                ? Math.Max(1.5, Math.Round(targetDuration - currentStart, 2))
                : Math.Max(1.5, Math.Round(((double)segmentWords / totalWords) * targetDuration, 2));

            var end = Math.Round(currentStart + duration, 2);

            // Image Assignment under ArticleImageOnly Mode
            ArticleImageInputDto? assignedImage = null;
            string visualType;
            string motion;
            string cropType = "Center";
            bool isReused = false;

            if (validImages.Count > 0)
            {
                if (i < validImages.Count)
                {
                    assignedImage = validImages[i];
                    visualType = "Image";
                    motion = ChooseMotion(i, segment.Purpose);
                    cropType = "Center";
                    isReused = false;
                }
                else
                {
                    // Lacking enough images for 8-10 scenes: Reuse suitable article image with differing motion/crop
                    assignedImage = validImages[i % validImages.Count];
                    visualType = "Image";
                    isReused = true;
                    cropType = (i % 3 == 0) ? "CloseUp" : (i % 3 == 1) ? "Top" : "Bottom";
                    motion = ChooseAlternativeMotion(i, segment.Purpose);
                }
            }
            else
            {
                // Article has zero suitable images: Fallback to Typography, notify user
                assignedImage = null;
                visualType = "Typography";
                motion = "ZoomIn";
                cropType = "Center";
                isReused = false;
            }

            var transition = ChooseTransition(i);

            scenes.Add(new VideoScenePlanDto
            {
                SceneNumber = i + 1,
                StartTimeSeconds = Math.Round(currentStart, 2),
                EndTimeSeconds = end,
                VoiceText = segment.VoiceText,
                OnScreenText = segment.OnScreenText,
                SelectedArticleImageId = assignedImage?.Id,
                ImageUrl = assignedImage?.OriginalImageUrl,
                ImageCaption = assignedImage?.Caption,
                VisualType = visualType,
                MotionType = motion,
                TransitionType = transition,
                CropType = cropType,
                IsImageReused = isReused,
                ScenePurpose = segment.Purpose
            });

            currentStart = end;
        }

        return Task.FromResult(scenes);
    }

    /// <summary>Seconds of narration per scene (one picture change every ~7 s).</summary>
    private const double SecondsPerScene = 7.0;

    private static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Splits the real voice-over into consecutive scenes. Every scene's text is exactly what the
    /// narrator says during it — nothing is invented to reach a fixed scene count.
    /// </summary>
    private static List<NarrativeSegment> BuildNarrativeSegments(EditorialScriptResult script, double narrationSeconds)
    {
        var voiceOver = string.IsNullOrWhiteSpace(script.VoiceOverScript)
            ? string.Join(" ", new[] { script.Hook, script.KeyFacts, script.Analysis, script.Conclusion }
                .Where(x => !string.IsNullOrWhiteSpace(x)))
            : script.VoiceOverScript;

        var sentences = Regex.Split(voiceOver.Trim(), @"(?<=[.!?…])\s+")
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
        if (sentences.Count == 0) sentences.Add(voiceOver.Trim());

        var desired = (int)Math.Round(narrationSeconds / SecondsPerScene);
        var sceneCount = Math.Clamp(desired, 1, sentences.Count);

        // Greedy grouping of consecutive sentences into scenes with roughly equal word counts
        var totalWords = Math.Max(1, sentences.Sum(CountWords));
        var groups = new List<List<string>>();
        var current = new List<string>();
        var wordsSoFar = 0;
        for (var i = 0; i < sentences.Count; i++)
        {
            current.Add(sentences[i]);
            wordsSoFar += CountWords(sentences[i]);
            var remainingSentences = sentences.Count - i - 1;
            var remainingScenes = sceneCount - groups.Count - 1;
            var boundary = (double)totalWords * (groups.Count + 1) / sceneCount;
            if (remainingScenes > 0 && (wordsSoFar >= boundary || remainingSentences == remainingScenes))
            {
                groups.Add(current);
                current = new List<string>();
            }
        }
        if (current.Count > 0) groups.Add(current);

        var list = new List<NarrativeSegment>();
        for (var i = 0; i < groups.Count; i++)
        {
            var text = string.Join(" ", groups[i]);
            var purpose = i == 0 ? "Hook"
                : i == groups.Count - 1 && groups.Count > 2 ? "Conclusion"
                : i <= groups.Count / 2 ? "Fact"
                : "Analysis";
            list.Add(new NarrativeSegment
            {
                Purpose = purpose,
                VoiceText = text,
                OnScreenText = i == 0 && !string.IsNullOrWhiteSpace(script.VideoTitle)
                    ? TruncateHighlight(script.VideoTitle, 40)
                    : TruncateHighlight(groups[i][0], 40)
            });
        }
        return list;
    }

    private static string ChooseMotion(int index, string purpose)
    {
        return purpose switch
        {
            "Hook" => "ZoomIn",
            "Analysis" => "KenBurns",
            "Conclusion" => "ZoomOut",
            _ => (index % 2 == 0) ? "PanLeft" : "PanRight"
        };
    }

    private static string ChooseAlternativeMotion(int index, string purpose)
    {
        // When reusing image, choose alternate subtle motion to prevent visual monotony
        return (index % 4) switch
        {
            0 => "PanRight",
            1 => "ZoomOut",
            2 => "PanLeft",
            _ => "ZoomIn"
        };
    }

    private static string ChooseTransition(int index)
    {
        if (index == 0) return "Cut";
        return (index % 2 == 0) ? "Fade" : "Slide";
    }

    private static string TruncateHighlight(string text, int maxLength)
    {
        var clean = text.ToUpperInvariant().Replace("\n", " ").Trim();
        if (clean.Length <= maxLength) return clean;
        return clean.Substring(0, maxLength - 3) + "...";
    }

    private class NarrativeSegment
    {
        public string Purpose { get; set; } = "Fact";
        public string VoiceText { get; set; } = string.Empty;
        public string OnScreenText { get; set; } = string.Empty;
    }
}
