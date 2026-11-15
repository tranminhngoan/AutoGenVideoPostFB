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
        var targetDuration = Math.Clamp(input.TargetDurationSeconds > 0 ? input.TargetDurationSeconds : 55, 45, 60);

        // Filter valid article images (current article only, not rejected, not unsuitable)
        var validImages = input.AvailableImages
            .Where(img => img.RightsStatus != "Rejected" && img.Category != "Unsuitable" && img.SelectedForVideo)
            .OrderBy(img => img.Order)
            .ToList();

        // Break voiceover into 6-10 narrative segments
        var segments = BuildNarrativeSegments(script);
        var sceneCount = Math.Clamp(segments.Count, 6, 10);

        // Allocate duration across scenes proportional to speech length
        var totalWords = segments.Sum(s => s.VoiceText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        if (totalWords <= 0) totalWords = 1;

        var scenes = new List<VideoScenePlanDto>();
        double currentStart = 0;
        int imageIndex = 0;

        for (int i = 0; i < sceneCount; i++)
        {
            var segment = segments[i];
            var segmentWords = segment.VoiceText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            
            // Calculate scene duration (minimum 4s, maximum 9s)
            double duration;
            if (i == sceneCount - 1)
            {
                duration = Math.Round(targetDuration - currentStart, 2);
                if (duration < 4.0) duration = 4.0;
            }
            else
            {
                duration = Math.Round(((double)segmentWords / totalWords) * targetDuration, 2);
                duration = Math.Clamp(duration, 4.0, 9.0);
            }

            var end = Math.Round(currentStart + duration, 2);

            // Assign image or fallback to typography/charts
            ArticleImageInputDto? assignedImage = null;
            string visualType;

            if (imageIndex < validImages.Count)
            {
                assignedImage = validImages[imageIndex++];
                visualType = "Image";
            }
            else
            {
                // Strict Master Plan rule: No image? Use Typography or MetricChart/Timeline. Never fake AI/external image!
                assignedImage = null;
                visualType = segment.Purpose == "Analysis" ? "MetricChart" : "Typography";
            }

            var motion = ChooseMotion(i, segment.Purpose);
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
                ScenePurpose = segment.Purpose
            });

            currentStart = end;
        }

        // Final normalization to ensure total duration matches 45-60s
        if (scenes.Count > 0)
        {
            var lastScene = scenes[^1];
            if (lastScene.EndTimeSeconds < 45.0)
            {
                lastScene.EndTimeSeconds = 48.0;
            }
            else if (lastScene.EndTimeSeconds > 60.0)
            {
                lastScene.EndTimeSeconds = 58.0;
            }
        }

        return Task.FromResult(scenes);
    }

    private static List<NarrativeSegment> BuildNarrativeSegments(EditorialScriptResult script)
    {
        var list = new List<NarrativeSegment>();

        // Scene 1: The Hook
        list.Add(new NarrativeSegment
        {
            Purpose = "Hook",
            VoiceText = script.Hook,
            OnScreenText = TruncateHighlight(script.VideoTitle.Replace("TIÊU ĐIỂM:", "").Trim(), 30)
        });

        // Scene 2 & 3: Key Facts (Breakdown)
        var facts = script.KeyFacts
            .Split(new[] { '\n', '-', '•' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.Length > 10)
            .ToList();

        if (facts.Count > 0)
        {
            list.Add(new NarrativeSegment
            {
                Purpose = "Fact",
                VoiceText = facts[0],
                OnScreenText = TruncateHighlight(facts[0], 25)
            });
        }
        else
        {
            list.Add(new NarrativeSegment
            {
                Purpose = "Fact",
                VoiceText = "Diễn biến sự kiện ghi nhận nhiều dữ kiện đáng chú ý được công bố.",
                OnScreenText = "DỮ KIỆN QUAN TRỌNG"
            });
        }

        if (facts.Count > 1)
        {
            list.Add(new NarrativeSegment
            {
                Purpose = "Fact",
                VoiceText = facts[1],
                OnScreenText = TruncateHighlight(facts[1], 25)
            });
        }

        // Scene 4: Context
        if (!string.IsNullOrWhiteSpace(script.Context))
        {
            list.Add(new NarrativeSegment
            {
                Purpose = "Context",
                VoiceText = script.Context,
                OnScreenText = "BỐI CẢNH SỰ KIỆN"
            });
        }

        // Scene 5 & 6: Editorial Analysis
        if (!string.IsNullOrWhiteSpace(script.Analysis))
        {
            var analysisSentences = script.Analysis.Split(new[] { '.', '!' }, StringSplitOptions.RemoveEmptyEntries);
            if (analysisSentences.Length > 1)
            {
                list.Add(new NarrativeSegment
                {
                    Purpose = "Analysis",
                    VoiceText = analysisSentences[0].Trim() + ".",
                    OnScreenText = "GÓC NHÌN BIÊN TẬP"
                });
                list.Add(new NarrativeSegment
                {
                    Purpose = "Analysis",
                    VoiceText = string.Join(". ", analysisSentences.Skip(1)).Trim(),
                    OnScreenText = "ĐÁNH GIÁ TÁC ĐỘNG"
                });
            }
            else
            {
                list.Add(new NarrativeSegment
                {
                    Purpose = "Analysis",
                    VoiceText = script.Analysis,
                    OnScreenText = "GÓC NHÌN BIÊN TẬP"
                });
            }
        }

        // Scene 7+: Conclusion & Source Attribution
        list.Add(new NarrativeSegment
        {
            Purpose = "Conclusion",
            VoiceText = $"{script.Conclusion} {script.SourceAttribution}.",
            OnScreenText = "THEO DÕI THÊM BÌNH LUẬN"
        });

        // Ensure at least 6 scenes
        while (list.Count < 6)
        {
            list.Insert(list.Count - 1, new NarrativeSegment
            {
                Purpose = "Fact",
                VoiceText = "Thông tin chi tiết đang tiếp tục được cập nhật theo nguồn tin chính thống.",
                OnScreenText = "CẬP NHẬT MỚI NHẤT"
            });
        }

        // Cap at 10 scenes
        if (list.Count > 10)
        {
            list = list.Take(10).ToList();
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

    private static string ChooseTransition(int index)
    {
        if (index == 0) return "Cut";
        return (index % 2 == 0) ? "Slide" : "Fade";
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
