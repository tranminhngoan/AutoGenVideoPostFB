namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>
/// A content theme the owner picks for a project. The analysis stays a neutral observation of the video; the theme is
/// added by the prompt engine afterwards so every scene comes out in the look the channel wants: who the people look
/// like (ethnicity / skin — overriding what the video showed), extra look notes, setting and grade hints, and what to avoid.
/// A theme with a <see cref="Locale"/> is strict: the AI review translates every person, garment, house, vehicle, object
/// and piece of writing of the (often foreign) reference into that place and era, and each prompt carries
/// <see cref="LocaleRule"/> and <see cref="WritingRule"/> — never cut.
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
    string Avoid,
    string[]? OutdoorDetails = null,
    string[]? IndoorDetails = null,
    bool PreferWide = false,
    string? Locale = null,
    string? LocaleRule = null,
    string? WritingRule = null);

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
            "Người Việt miền Tây Nam Bộ da rám nắng, làng quê sông nước nghèo thập niên 90, áo bà ba, nón lá, mộc mạc, hoài niệm.",
            Ethnicity: "Mekong Delta Vietnamese (Kinh, Southeast Asian)",
            Skin: "sun-tanned brown skin from working outdoors",
            Look: "Mekong Delta country people of the 1990s in authentic southern Vietnamese clothes (áo bà ba, khăn rằn scarf, nón lá hat, or simple faded shirts and shorts), worn and faded cloth, honest hard-working rural life",
            SettingHint: "a poor 1990s Mekong Delta village in southern Vietnam (miền Tây sông nước): canals and small rivers, nipa-palm thatched houses with leaf walls, coconut groves, monkey bridges, rice fields",
            StyleHint: "nostalgic warm golden afternoon light, slightly faded film tones with soft grain, documentary realism, humble and heartfelt",
            Avoid: "Chinese, Korean, Japanese, Thai or Western people, clothes, houses, vehicles or decorations; Chinese characters, hanzi, Japanese, Korean or English writing; snow, frost, padded winter coats, northern-style brick courtyard houses; modern buildings, smartphones, modern cars or motorbikes, branded clothes, plastic toys, pale skin",
            OutdoorDetails: new[]
            {
                "a calm brown canal lined with nipa palms and coconut trees", "a narrow bamboo monkey bridge over the canal",
                "a small wooden sampan tied at the muddy bank", "water hyacinth drifting slowly on the river",
                "green rice paddies stretching to the horizon", "nipa-palm thatched houses with leaf walls by the water",
                "a packed-earth yard with chickens and ducks", "big brown glazed clay water jars under the eaves",
                "banana and areca palms around the house", "yellow sesbania flowers along the canal bank",
                "a fish pond with a wooden plank footbridge", "bamboo fish traps and nets drying on a bamboo fence",
                "a hammock strung between two coconut trees", "a dirt path along the canal dyke"
            },
            IndoorDetails: new[]
            {
                "a nipa-palm thatched roof on wooden rafters", "walls of dried palm leaves with warm light slipping through the gaps",
                "a packed-earth floor", "a portable clay stove with a soot-blackened pot",
                "stacks of dry coconut husks and firewood", "a wide wooden plank bed with a woven sedge mat",
                "a hammock hung across the room", "a low wooden table with chipped enamel bowls and chopsticks",
                "an oil lamp casting a soft warm glow", "a small ancestor altar with incense sticks and fruit",
                "woven bamboo baskets and winnowing trays on the wall"
            },
            PreferWide: true,
            Locale: "the rural Mekong Delta (miền Tây sông nước, Nam Bộ) of southern Vietnam in the 1990s. People: ethnic Vietnamese (Kinh) " +
                    "with sun-tanned skin and Southeast Asian faces, never Chinese-, Korean-, Japanese- or Western-looking. Clothes: adults in " +
                    "áo bà ba (loose collarless button-front shirt) with loose long trousers in faded plain colours, a checkered khăn rằn scarf, " +
                    "a nón lá conical palm-leaf hat; elderly women with hair in a bun; children in faded short-sleeve shirts and shorts or a " +
                    "simple dress; pupils in white shirts with dark-blue shorts, trousers or skirts and a red scarf; barefoot, wooden clogs or " +
                    "rubber sandals. The climate is tropical — no snow, frost, padded coats, wool hats or scarves against cold. Houses: " +
                    "nipa-palm thatched houses with leaf or plank walls, some tiled or corrugated-tin roofs, houses on stilts along the canal, " +
                    "packed-earth yards, coconut and banana trees, canals, monkey bridges, rice fields. Vehicles: wooden sampans, small motor " +
                    "canoes, river ferries, old black bicycles, an old Honda Cub motorbike — no cars, tractors or Chinese three-wheelers. " +
                    "Objects: portable clay stoves, clay pots, glazed water jars, enamel bowls, chopsticks, bamboo baskets, conical hats, " +
                    "hammocks, fishing traps, oil lamps, an ancestor altar with incense. School: a single-storey yellow-walled classroom with a " +
                    "tiled roof, wooden desks and benches, a black chalkboard. Food: rice, fish, vegetables, sweet potatoes, coconuts, bánh.",
            LocaleRule: "Vietnam: everything is authentically southern Vietnamese (Mekong Delta, 1990s) — the people, their clothes, the houses, boats, bicycles, tools, food and every object; nothing Chinese or foreign.",
            WritingRule: "Writing: any visible writing (chalkboard, book, sign, label) is Vietnamese in Latin letters with diacritics or simple numbers only — never Chinese characters or any foreign language."),
    };

    public static AiStudioTopic? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? id) => Find(id)?.Id ?? None;
}
