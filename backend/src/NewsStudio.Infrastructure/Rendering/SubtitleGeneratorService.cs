using System.Text;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Rendering;

public class SubtitleGeneratorService : ISubtitleGeneratorService
{
    public async Task<string> GenerateSrtAsync(
        List<SubtitleCueDto> cues,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            sb.AppendLine((i + 1).ToString());
            sb.AppendLine($"{FormatSrtTime(cue.StartTimeSeconds)} --> {FormatSrtTime(cue.EndTimeSeconds)}");
            sb.AppendLine(cue.Text.Trim());
            sb.AppendLine();
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
        return outputPath;
    }

    public async Task<string> GenerateVttAsync(
        List<SubtitleCueDto> cues,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();

        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            sb.AppendLine((i + 1).ToString());
            sb.AppendLine($"{FormatVttTime(cue.StartTimeSeconds)} --> {FormatVttTime(cue.EndTimeSeconds)}");
            sb.AppendLine(cue.Text.Trim());
            sb.AppendLine();
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
        return outputPath;
    }

    private static string FormatSrtTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }

    private static string FormatVttTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
