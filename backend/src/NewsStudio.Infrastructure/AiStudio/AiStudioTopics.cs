namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>
/// A content theme the owner picks for a project. The analysis stays a neutral observation of the video; the theme is
/// added by the prompt engine afterwards so every scene comes out in the look the channel wants: who the people look
/// like (ethnicity / skin — overriding what the video showed), extra look notes, setting and grade hints, and what to avoid.
/// </summary>
public sealed record AiStudioTopic(
    string Id,
    string Label,
    string Description,
    string Ethnicity,
    string Skin,
    string Look,
    string? SettingHint,
    string StyleHint,
    string Avoid);

public static class AiStudioTopics
{
    public const string None = "";
    public const string AsianBeauty = "asian-beauty";
    public const string VietnamChildhood = "vn-childhood";

    public static readonly AiStudioTopic[] All =
    {
        new(AsianBeauty,
            "Gái xinh châu Á",
            "Người đẹp châu Á (Hàn, Nhật, Việt), da trắng sáng, trang điểm tự nhiên, ánh sáng đẹp, tinh tế.",
            Ethnicity: "East Asian (Korean, Japanese or Vietnamese look)",
            Skin: "fair porcelain skin with a natural healthy glow",
            Look: "beautiful young East Asian appearance, delicate natural features, soft natural makeup, glossy well-kept hair, graceful elegant presence",
            SettingHint: null,
            StyleHint: "bright airy K-drama style look, soft flattering diffused light, gentle skin glow, clean pastel-leaning palette, tasteful and elegant",
            Avoid: "Western or European facial features, tanned skin, heavy makeup, vulgar or sexualized framing, revealing close-ups of the body"),
        new(VietnamChildhood,
            "Ký ức tuổi thơ Việt Nam",
            "Người Việt da rám nắng, làng quê nghèo thập niên 90, mộc mạc, cực khổ, hoài niệm.",
            Ethnicity: "Vietnamese (Southeast Asian)",
            Skin: "sun-tanned brown skin from working outdoors",
            Look: "Vietnamese countryside people of the 1990s, simple worn and faded clothes, dust on skin and clothes, honest hard-working rural life",
            SettingHint: "a poor rural Vietnamese village in the 1990s: thatched or bamboo houses, packed-earth yards, rice fields, banana trees, simple handmade tools",
            StyleHint: "nostalgic warm golden afternoon light, slightly faded film tones with soft grain, documentary realism, humble and heartfelt",
            Avoid: "modern buildings, smartphones, cars or motorbikes of today, new branded clothes, plastic toys, pale or Western-looking people"),
    };

    public static AiStudioTopic? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? id) => Find(id)?.Id ?? None;
}
