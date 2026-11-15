using Microsoft.Extensions.Configuration;

namespace NewsStudio.Infrastructure.Common;

/// <summary>
/// Controls whether external integrations (AI, TTS, FFmpeg, Meta Graph API) may fall back to
/// locally simulated output when they are unavailable.
///
/// Disabled by default: in normal use a missing API key, a failed render or a rejected Facebook
/// upload must surface as a real error instead of silently producing placeholder data.
/// The automated test suite enables it (Simulation:Enabled=true) so tests can run offline.
/// </summary>
public static class SimulationMode
{
    public const string ConfigKey = "Simulation:Enabled";

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>(ConfigKey, false);
}
