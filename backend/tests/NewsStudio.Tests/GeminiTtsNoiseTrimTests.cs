using FluentAssertions;
using NewsStudio.Infrastructure.Rendering;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>Gemini TTS clips end with a short burst of static after the speech; it must be cut, speech must not.</summary>
public class GeminiTtsNoiseTrimTests
{
    private const int Rate = 24000;

    private static IEnumerable<short> Tone(double seconds, double hz, short amp) =>
        Enumerable.Range(0, (int)(seconds * Rate)).Select(i => (short)(amp * Math.Sin(2 * Math.PI * hz * i / Rate)));

    private static IEnumerable<short> Silence(double seconds) => Enumerable.Repeat((short)0, (int)(seconds * Rate));

    private static IEnumerable<short> Static(double seconds)
    {
        var rng = new Random(7);
        return Enumerable.Range(0, (int)(seconds * Rate)).Select(_ => (short)rng.Next(-25000, 25000));
    }

    private static byte[] Pcm(params IEnumerable<short>[] parts) =>
        parts.SelectMany(p => p).SelectMany(BitConverter.GetBytes).ToArray();

    [Fact]
    public void Cuts_the_static_burst_after_the_final_silence()
    {
        // Real clips: speech, ~340 ms silence, then 40 ms static, 30 ms silence, 90 ms static.
        var pcm = Pcm(Tone(1.0, 180, 8000), Silence(0.34), Static(0.04), Silence(0.03), Static(0.09));

        var trimmed = GeminiTtsService.TrimTrailingNoiseBurst(pcm, out var ms);

        ms.Should().BeInRange(150, 170);
        trimmed.Length.Should().Be((int)((1.0 + 0.34) * Rate) * 2);
    }

    [Fact]
    public void Keeps_a_clip_whose_speech_runs_to_the_end()
    {
        var pcm = Pcm(Tone(1.0, 180, 8000));
        GeminiTtsService.TrimTrailingNoiseBurst(pcm, out var ms).Should().Equal(pcm);
        ms.Should().Be(0);
    }

    [Fact]
    public void Keeps_a_short_last_word_after_a_pause()
    {
        // A voiced word (low zero-crossing rate) after a breath is speech, not static.
        var pcm = Pcm(Tone(1.0, 180, 8000), Silence(0.08), Tone(0.2, 160, 9000));
        GeminiTtsService.TrimTrailingNoiseBurst(pcm, out var ms).Should().Equal(pcm);
        ms.Should().Be(0);
    }
}
