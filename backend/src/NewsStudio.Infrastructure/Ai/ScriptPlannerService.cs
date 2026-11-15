using System;
using System.Collections.Generic;
using System.Linq;
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
        // Mode A: ArticleImageOnly - Target duration 55–65s (target 60s)
        var targetDuration = Math.Clamp(input.TargetDurationSeconds > 0 ? input.TargetDurationSeconds : 60, 55, 65);

        // Filter valid article images: strictly from current article only, not rejected, not unsuitable
        var validImages = input.AvailableImages
            .Where(img => img.RightsStatus != "Rejected" && img.Category != "Unsuitable" && img.SelectedForVideo)
            .OrderBy(img => img.Order)
            .ToList();

        // Break script into 8-10 narrative segments (Rule 4: 8–10 scenes)
        var segments = BuildNarrativeSegments(script);
        var sceneCount = Math.Clamp(segments.Count, 8, 10);

        // Allocate duration across scenes proportional to speech length
        var totalWords = segments.Take(sceneCount).Sum(s => s.VoiceText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        if (totalWords <= 0) totalWords = 1;

        var scenes = new List<VideoScenePlanDto>();
        double currentStart = 0;

        for (int i = 0; i < sceneCount; i++)
        {
            var segment = segments[i];
            var segmentWords = segment.VoiceText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            // Calculate scene duration (minimum 4.5s, maximum 8.5s)
            double duration;
            if (i == sceneCount - 1)
            {
                duration = Math.Round(targetDuration - currentStart, 2);
                if (duration < 4.5) duration = 4.5;
            }
            else
            {
                duration = Math.Round(((double)segmentWords / totalWords) * targetDuration, 2);
                duration = Math.Clamp(duration, 4.5, 8.5);
            }

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

        // Final normalization to ensure total duration sits in 55–65s (target 60s)
        if (scenes.Count > 0)
        {
            var lastScene = scenes[^1];
            if (lastScene.EndTimeSeconds < 55.0)
            {
                lastScene.EndTimeSeconds = 58.0;
            }
            else if (lastScene.EndTimeSeconds > 65.0)
            {
                lastScene.EndTimeSeconds = 62.0;
            }
        }

        return Task.FromResult(scenes);
    }

    private static List<NarrativeSegment> BuildNarrativeSegments(EditorialScriptResult script)
    {
        var list = new List<NarrativeSegment>();

        // Scene 1: The Hook (3-4 seconds opening)
        list.Add(new NarrativeSegment
        {
            Purpose = "Hook",
            VoiceText = script.Hook,
            OnScreenText = TruncateHighlight(script.VideoTitle.Replace("TIÊU ĐIỂM:", "").Trim(), 28)
        });

        // Scene 2, 3, 4: Key Facts Breakdown
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
                VoiceText = "Diễn biến sự kiện ghi nhận nhiều dữ kiện đáng chú ý vừa được cơ quan chức năng công bố.",
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

        if (facts.Count > 2)
        {
            list.Add(new NarrativeSegment
            {
                Purpose = "Fact",
                VoiceText = facts[2],
                OnScreenText = TruncateHighlight(facts[2], 25)
            });
        }

        // Scene 5: Context & Background
        if (!string.IsNullOrWhiteSpace(script.Context))
        {
            list.Add(new NarrativeSegment
            {
                Purpose = "Context",
                VoiceText = script.Context,
                OnScreenText = "BỐI CẢNH ĐÁNG CHÚ Ý"
            });
        }
        else
        {
            list.Add(new NarrativeSegment
            {
                Purpose = "Context",
                VoiceText = "Vấn đề này diễn ra trong bối cảnh các quy định mới đang được triển khai đồng bộ.",
                OnScreenText = "BỐI CẢNH THỰC TẾ"
            });
        }

        // Scene 6 & 7: Editorial Analysis
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
                    OnScreenText = "PHÂN TÍCH CHUYÊN MÔN"
                });
            }
        }
        else
        {
            list.Add(new NarrativeSegment
            {
                Purpose = "Analysis",
                VoiceText = "Giới chuyên gia nhận định đây là bước ngoặt quan trọng mang lại nhiều thay đổi lớn.",
                OnScreenText = "ĐÁNH GIÁ TÁC ĐỘNG"
            });
        }

        // Scene 8: Outlook & Implication
        list.Add(new NarrativeSegment
        {
            Purpose = "Analysis",
            VoiceText = "Các bên liên quan đang tiếp tục theo dõi sát sao tiến độ nhằm đảm bảo hiệu quả tối đa.",
            OnScreenText = "XU HƯỚNG TIẾP THEO"
        });

        // Scene 9: Conclusion & Summary
        list.Add(new NarrativeSegment
        {
            Purpose = "Conclusion",
            VoiceText = script.Conclusion,
            OnScreenText = "TỔNG KẾT VẤN ĐỀ"
        });

        // Scene 10: Source Attribution & Call-to-action
        var attributionText = !string.IsNullOrWhiteSpace(script.SourceAttribution)
            ? $"{script.SourceAttribution}. Hãy để lại bình luận và theo dõi kênh để cập nhật tin tức mới nhất."
            : "Thông tin được cập nhật từ báo chí chính thống. Nhấn theo dõi để xem thêm tin nóng.";

        list.Add(new NarrativeSegment
        {
            Purpose = "Conclusion",
            VoiceText = attributionText,
            OnScreenText = "THEO DÕI TIN MỚI"
        });

        // Enforce strictly 8 to 10 scenes
        while (list.Count < 8)
        {
            list.Insert(list.Count - 2, new NarrativeSegment
            {
                Purpose = "Fact",
                VoiceText = "Toàn bộ diễn biến tiếp theo sẽ được chuyển tải nhanh chóng trong các bản tin tiếp theo.",
                OnScreenText = "CẬP NHẬT LIÊN TỤC"
            });
        }

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
