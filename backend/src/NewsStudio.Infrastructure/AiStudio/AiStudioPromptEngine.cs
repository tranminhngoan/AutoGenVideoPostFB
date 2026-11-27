using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>Everything the prompt of one scene is built from.</summary>
public sealed record ScenePromptInput(
    int Index,
    double StartSeconds,
    double EndSeconds,
    int TargetSeconds,
    bool IsContinuation,
    bool HasPreviousScene,
    JsonObject Analysis,
    JsonObject Measured,
    JsonArray Characters,
    JsonArray Environments,
    JsonObject Style,
    string Mode,
    AiStudioVariationDto Variation,
    string GlobalPrompt,
    string NegativePrompt,
    string Topic = "",
    string? Narration = null);

/// <summary>
/// Cinematic prompt engine for Seedance (deterministic, no AI call, so a prompt can be rebuilt instantly when the user
/// changes the mode or a bible). It writes, in order: subject, character appearance, environment, main action, timed
/// action sequence, body mechanics, facial expression, camera angle / movement, composition, lighting, colour, depth
/// of field, physical realism, motion continuity, visual quality and the constraints. Only facts of the analysis are
/// used ("uncertain" fields are left out); the camera movement is the measured one. Modes:
/// Faithful (A) — keeps subject, actions, order, emotion, rhythm and layout, described in new words;
/// Creative (B) — the same actions with the user's chosen character / wardrobe / setting / camera / lighting / style;
/// Cinematic (C) — the same content with upgraded camera language, lighting, grade, depth and photorealism.
/// Seedance has no separate negative prompt, so the constraints end the prompt as "Avoid: …".
/// </summary>
public static class AiStudioPromptEngine
{
    public const string Faithful = "Faithful";
    public const string Creative = "Creative";
    public const string Cinematic = "Cinematic";
    public static readonly string[] Modes = { Faithful, Creative, Cinematic };

    public const string DefaultNegative =
        "on-screen text, subtitles, captions, watermark, logo, brand names, extra people, duplicated limbs, distorted hands or face, flicker, sudden cuts, jump cuts";

    /// <summary>Always avoided, whatever the user's list says: the usual video-model failures.</summary>
    public const string AlwaysAvoid =
        "extra fingers, extra or missing limbs, melting face, morphing body or objects, smeared motion, unsafe or illogical actions";

    /// <summary>One action of the reference per this many seconds of clip at most: crowded clips break the motion.</summary>
    public const double SecondsPerAction = 2.5;

    /// <summary>Keeps a prompt under what the model reads well (characters).</summary>
    public const int MaxPromptChars = 2400;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string NormalizeMode(string? mode) =>
        Modes.FirstOrDefault(m => string.Equals(m, mode?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Faithful;

    /// <summary>Creative mode needs at least one change, else it is the faithful prompt.</summary>
    public static bool HasVariation(AiStudioVariationDto? v) =>
        v != null && new[] { v.Character, v.Wardrobe, v.Setting, v.Camera, v.Lighting, v.Style }.Any(x => !string.IsNullOrWhiteSpace(x));

    /// <summary>The default global prompt from the style bible (the user may rewrite it).</summary>
    public static string DefaultGlobalPrompt(JsonObject style)
    {
        // only what is true for every scene: the overall look (palette / light differ per scene and are in each prompt)
        var parts = new List<string>();
        AddIf(parts, Global(Field(style, "visual_style")));
        AddIf(parts, Global(Field(style, "realism_level")) is { } r ? $"realism: {r}" : null);
        return parts.Count == 0 ? "Photorealistic live-action footage." : Sentence(string.Join("; ", parts));
    }

    /// <summary>Drops the clauses that talk about one scene ("… in Scene 2") — they would contradict the other scenes.</summary>
    private static string? Global(string? text)
    {
        if (text == null) return null;
        var clauses = Regex.Split(text, @"(?<=[.;,])\s+").Where(c => !Regex.IsMatch(c, @"\bscenes?\s*\d", RegexOptions.IgnoreCase));
        return Clean(string.Join(" ", clauses));
    }

    public static string Build(ScenePromptInput s)
    {
        var mode = NormalizeMode(s.Mode);
        var a = s.Analysis;
        var v = s.Variation ?? new AiStudioVariationDto();
        var creative = mode == Creative;
        var cinematic = mode == Cinematic;
        var topic = AiStudioTopics.Find(s.Topic);
        var parts = new List<string>();

        // 0. the user's global prompt (style lock shared by every scene)
        AddIf(parts, Clean(s.GlobalPrompt) is { } g ? Sentence(g) : null);

        // 1–2. subject + character appearance
        var cast = CastOf(s);
        var people = Arr(a, "characters");
        if (creative && Clean(v.Character) is { } who)
        {
            var wardrobe = Clean(v.Wardrobe);
            parts.Add(Sentence($"Subject: {who}" + (wardrobe != null ? $", wearing {wardrobe}" : "")));
        }
        else if (cast.Count > 0)
        {
            var descs = cast.Select(c => DescribeCharacter(c, creative ? Clean(v.Wardrobe) : null, topic)).Where(d => d.Length > 0).ToList();
            if (descs.Count > 0) parts.Add(Sentence((descs.Count == 1 ? "Subject: " : $"Subjects ({descs.Count}): ") + string.Join("; ", descs)));
        }
        else if (people.Count > 0)
        {
            var descs = people.OfType<JsonObject>().Select(c => DescribeCharacter(c, creative ? Clean(v.Wardrobe) : null, topic)).Where(d => d.Length > 0).ToList();
            if (descs.Count > 0) parts.Add(Sentence((descs.Count == 1 ? "Subject: " : $"Subjects ({descs.Count}): ") + string.Join("; ", descs)));
        }
        // the channel's theme: how the people must look, whatever the reference showed
        var hasPeople = cast.Count > 0 || people.Count > 0 || (creative && Clean(v.Character) != null);
        if (topic != null && hasPeople && !(creative && Clean(v.Character) != null)) parts.Add(Sentence($"Look: {topic.Look}"));

        // 3. environment: the analysis + the theme's countryside, rich and the same for every scene of one place
        if (creative && Clean(v.Setting) is { } setting) parts.Add(Sentence($"Setting: {setting}"));
        else
        {
            var bibleEnv = EnvironmentOf(s);
            var env = DescribeEnvironment(a, bibleEnv);
            if (RichDetails(topic, a, bibleEnv, s.Index) is { Length: > 0 } rich)
            {
                // the place, the era, the village's details first — the reference's own small details after (cut first when long)
                var place = Field(bibleEnv, "location") ?? Field(Obj(a, "environment"), "location");
                var era = topic!.SettingHint?.Split(':')[0];
                env = Join(", ", place, era, rich, env);
            }
            else if (topic?.SettingHint != null) env = Join(", ", env, topic.SettingHint);
            AddIf(parts, env.Length > 0 ? Sentence($"Setting: {env}") : null);
        }

        // 4–5. main action + timed sequence (scaled to the clip length, one action per ~2.5 s at most)
        AddIf(parts, Field(a, "summary") is { } summary ? Sentence($"Action: {StripNames(summary)}") : null);
        var seq = Sequence(s);
        if (seq.Length > 0) parts.Add(Sentence($"Sequence: {seq}"));

        // 5b. the story told by the narration over this scene (read separately by a voice, never shown or lip-synced)
        if (NarrationLine(s.Narration) is { } story) parts.Add(story);

        // 6. body mechanics (the coordination only: the rest of the analysis is noise for the video model)
        var body = Obj(a, "body_movement");
        if (Field(body, "coordination") is { } coordination)
            parts.Add(Sentence("Body mechanics: " + coordination + (cinematic ? "; real weight, momentum and follow-through" : "")));

        // 7. facial expression
        var face = Obj(a, "facial_expression");
        AddIf(parts, Join(", ", Field(face, "expression"), Field(face, "emotion")) is { Length: > 0 } f ? Sentence($"Expression: {f}") : null);

        // 8–9. camera angle, shot size, movement (measured) — wider for a theme that shows the place
        parts.Add(Sentence(Camera(s, mode, v, topic?.PreferWide == true && hasPeople)));

        // 10. composition
        var comp = Obj(a, "composition");
        var composition = Join(", ", Field(comp, "subject_placement"), Field(comp, "depth"));
        if (topic?.PreferWide == true && hasPeople) composition = Join(", ", composition, "the person framed with plenty of the surroundings visible, clear foreground, midground and background");
        else if (cinematic) composition = Join(", ", composition, "balanced rule-of-thirds framing with clear foreground, midground and background layers");
        AddIf(parts, composition.Length > 0 ? Sentence($"Composition: {composition}") : null);

        // 11. lighting
        var light = Obj(a, "lighting");
        string lighting;
        if (creative && Clean(v.Lighting) is { } userLight) lighting = userLight;
        else
        {
            lighting = Join(", ", Field(light, "source"), Field(light, "direction") is { } d ? $"from {d}" : null,
                Field(light, "color_temperature") is { } ct ? $"{ct} color temperature" : null);
            if (cinematic) lighting = Join(", ", lighting, "soft motivated key light with gentle rim light separating the subject");
        }
        AddIf(parts, lighting.Length > 0 ? Sentence($"Lighting: {lighting}") : null);

        // 12. colour
        var color = Obj(a, "color");
        var measuredTone = Field(s.Measured, "tone");
        var colour = Join(", ", Field(color, "dominant"), Field(color, "tone") ?? measuredTone, Field(color, "grading"));
        if (cinematic) colour = Join(", ", colour, "cinematic color grade with rich filmic contrast, natural skin tones");
        if (creative && Clean(v.Style) is { } userStyle) colour = Join(", ", colour, userStyle);
        else if (topic != null) colour = Join(", ", colour, topic.StyleHint);
        AddIf(parts, colour.Length > 0 ? Sentence($"Color: {colour}") : null);

        // 13. depth of field — deep focus when the place matters, so the countryside stays sharp
        var dof = Obj(a, "depth_of_field");
        var depth = topic?.PreferWide == true
            ? "deep focus, the person and the surroundings both sharp"
            : Join(", ", Field(dof, "focus_subject") is { } fs ? $"focus on {fs}" : null, Field(dof, "background_blur") is { } bb ? $"background {bb}" : null);
        if (cinematic && topic?.PreferWide != true) depth = Join(", ", depth, "shallow depth of field with smooth natural bokeh");
        AddIf(parts, depth.Length > 0 ? Sentence($"Depth of field: {depth}") : null);

        // 14. behaviour: correct bodies, slow plausible motion, safe and logical actions for what happens here
        foreach (var rule in Behaviour(a, hasPeople)) parts.Add(rule);

        // 15. continuity
        var continuity = s.IsContinuation
            ? "Continuity: this continues the previous shot without a cut — same person, outfit, place, light and camera position, motion picks up exactly where it stopped"
            : s.HasPreviousScene
                ? "Continuity: same characters, outfits and look as the other scenes; one continuous take, smooth natural motion"
                : "Continuity: one continuous take, smooth natural motion";
        parts.Add(Sentence(continuity));

        // 16. visual quality
        parts.Add(Sentence(cinematic
            ? "Quality: photorealistic cinematic live-action footage, high detail, fine skin and fabric texture, sharp and stable, no artifacts"
            : "Quality: photorealistic live-action footage, natural detail, sharp and stable, no artifacts"));

        // 17. constraints (Seedance has no negative prompt field)
        var negative = Join(", ", Clean(s.NegativePrompt) ?? DefaultNegative, AlwaysAvoid, topic?.Avoid);
        var prompt = string.Join(" ", parts);
        var avoid = $" Avoid: {negative.TrimEnd('.')}.";
        if (prompt.Length + avoid.Length > MaxPromptChars) prompt = Shorten(parts, MaxPromptChars - avoid.Length);
        return (prompt + avoid).Trim();
    }

    // ───────────── pieces ─────────────

    /// <summary>Longest narration quoted in a prompt (characters); the visuals stay the main part.</summary>
    public const int MaxNarrationChars = 400;

    /// <summary>
    /// "Narration (voice-over, …): "…"." — the story of the scene as context for the picture. It is spoken off-screen
    /// by a separate voice, so the people must not lip-sync it and no text may appear.
    /// </summary>
    public static string? NarrationLine(string? narration)
    {
        var text = Clean(narration?.Replace('"', '\''));
        if (text == null) return null;
        if (text.Length > MaxNarrationChars) text = text[..MaxNarrationChars].TrimEnd() + "…";
        return $"Narration over this scene (off-screen voice-over, for the story only — nobody lip-syncs it, no text on screen): \"{text}\".";
    }

    /// <summary>The bible characters of this scene (by their scene numbers).</summary>
    private static List<JsonObject> CastOf(ScenePromptInput s) =>
        s.Characters.OfType<JsonObject>().Where(c => InScene(c, s.Index)).ToList();

    private static JsonObject? EnvironmentOf(ScenePromptInput s) =>
        s.Environments.OfType<JsonObject>().FirstOrDefault(e => InScene(e, s.Index));

    private static bool InScene(JsonObject o, int index) =>
        o["scenes"] is JsonArray arr && arr.Any(x => x is JsonValue jv && jv.TryGetValue<double>(out var n) && (int)n == index);

    private static string DescribeCharacter(JsonObject c, string? wardrobeOverride, AiStudioTopic? topic = null)
    {
        // "a Southeast Asian boy about 6 years old, light-brown skin, slim build, short black hair, wearing …, barefoot"
        var label = Field(c, "label");
        // the channel theme decides who the people are (e.g. Vietnamese with sun-tanned skin), whatever the reference showed
        var ethnicity = topic?.Ethnicity ?? Field(c, "apparent_ethnicity");
        var age = Field(c, "age_group");
        var gender = Field(c, "gender");
        var noun = label ?? gender;
        if (topic != null && ethnicity != null && age == null && noun == null)
            noun = Field(c, "appearance");
        if (ethnicity != null || age != null)
        {
            var who = Join(" ", ethnicity, noun ?? "person");
            var head = age != null ? $"{who} {(age.StartsWith("about", StringComparison.OrdinalIgnoreCase) || char.IsDigit(age[0]) ? age : $"({age})")}" : who;
            var skinText = topic?.Skin ?? Field(c, "skin_tone");
            var skin = skinText is { } st ? (st.Contains("skin", StringComparison.OrdinalIgnoreCase) ? st : $"{st} skin") : null;
            var hairText = Field(c, "hair");
            if (hairText != null && !hairText.Contains("hair", StringComparison.OrdinalIgnoreCase) && !hairText.Contains("bald", StringComparison.OrdinalIgnoreCase))
                hairText = $"{hairText.TrimEnd('.')} hair";
            var clothes = wardrobeOverride ?? Field(c, "clothing");
            var feet = Field(c, "footwear");
            var accessories = Field(c, "accessories");
            if (accessories != null && Regex.IsMatch(accessories, @"^\s*(none|no)\b", RegexOptions.IgnoreCase)) accessories = null;
            // older / edited bibles keep age and build inside "appearance"
            var body = Field(c, "build") ?? (age == null ? Field(c, "appearance") : null);
            return StripNames(Join(", ", head, skin, body, hairText, clothes != null ? $"wearing {clothes}" : null,
                wardrobeOverride == null ? feet : null, accessories != null ? $"with {accessories}" : null));
        }
        var appearance = Field(c, "appearance");
        var hair = Field(c, "hair");
        var clothing = wardrobeOverride ?? Field(c, "clothing");
        var acc = Field(c, "accessories");
        if (acc != null && Regex.IsMatch(acc, @"^\s*(none|no)\b", RegexOptions.IgnoreCase)) acc = null;
        if (hair != null && !hair.Contains("hair", StringComparison.OrdinalIgnoreCase) && !hair.Contains("bald", StringComparison.OrdinalIgnoreCase))
            hair = $"{hair.TrimEnd('.')} hair";
        var text = Join(", ", appearance ?? label, hair, clothing != null ? $"wearing {clothing}" : null, acc != null ? $"with {acc}" : null);
        return StripNames(text);
    }

    private static string DescribeEnvironment(JsonObject analysis, JsonObject? bibleEnv)
    {
        var env = Obj(analysis, "environment");
        var objects = env["objects"] is JsonArray arr ? string.Join(", ", arr.Select(x => x?.ToString()).Where(x => Clean(x) != null).Take(5)) : null;
        var background = Field(bibleEnv, "background") ?? Field(env, "background");
        return Join(", ",
            Field(bibleEnv, "location") ?? Field(env, "location"),
            Field(env, "landscape"), Field(env, "architecture"),
            background != null ? $"background: {background}" : null,
            Field(env, "foreground") is { } fg ? $"foreground: {fg}" : null,
            Clean(objects) is { } o ? $"objects: {o}" : null,
            Field(env, "weather") ?? Field(bibleEnv, "weather"),
            Field(env, "time_of_day") ?? Field(bibleEnv, "time_of_day"));
    }

    /// <summary>
    /// "0–2s …; 2–4s …": the reference's actions mapped onto the clip. At most one action per 2.5 s of clip — when the
    /// clip is shorter than the reference (a long video condensed to a minute) the longest actions are kept in their
    /// order and spread over the clip at a calm pace, instead of squeezing every action in (which breaks the motion).
    /// </summary>
    public static string Sequence(ScenePromptInput s)
    {
        var tl = Arr(s.Analysis, "timeline").OfType<JsonObject>().ToList();
        if (tl.Count == 0) return string.Empty;
        var srcDur = Math.Max(0.1, s.EndSeconds - s.StartSeconds);
        var actions = new List<(double From, double To, string Text)>();
        string? previous = null;
        foreach (var t in tl)
        {
            var action = Field(t, "action");
            var body = Field(t, "body");
            if (body != null && body.Contains("no person", StringComparison.OrdinalIgnoreCase)) body = null;
            var text = Join(", ", action, body);
            if (text.Length == 0 || string.Equals(text, previous, StringComparison.OrdinalIgnoreCase)) continue;
            previous = text;
            var a = Num(t, "start") ?? s.StartSeconds;
            var b = Num(t, "end") ?? s.EndSeconds;
            actions.Add((a - s.StartSeconds, Math.Max(a, b) - s.StartSeconds, StripNames(text)));
        }
        if (actions.Count == 0) return string.Empty;

        var room = Math.Max(1, (int)Math.Floor(s.TargetSeconds / SecondsPerAction));
        var scale = s.TargetSeconds / srcDur;
        if (actions.Count <= room && scale >= 0.75)
            return string.Join("; ", actions.Select(x =>
            {
                var from = Round(x.From * scale);
                var to = Round(x.To * scale);
                if (to <= from) to = Math.Min(s.TargetSeconds, from + 0.5);
                return $"{Fmt(from)}–{Fmt(to)}s {x.Text}";
            }));

        // condensed: the main actions only, each given an even share of the clip, done slowly
        var kept = actions.Select((x, i) => (x, i)).OrderByDescending(y => y.x.To - y.x.From).Take(room).OrderBy(y => y.i).Select(y => y.x).ToList();
        var share = s.TargetSeconds / (double)kept.Count;
        var items = kept.Select((x, i) => $"{Fmt(Round(i * share))}–{Fmt(Round((i + 1) * share))}s {x.Text}");
        return string.Join("; ", items) + " — at a calm, natural pace";
    }

    private static string Camera(ScenePromptInput s, string mode, AiStudioVariationDto v, bool wide = false)
    {
        var cam = Obj(s.Analysis, "camera");
        var angle = Field(cam, "angle");
        var size = Field(cam, "shot_size");
        if (wide) size = Wider(size);
        var moves = s.Measured["camera_movement"] is JsonArray arr ? arr.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList() : new List<string>();
        if (mode == Creative && Clean(v.Camera) is { } userCam)
            return $"Camera: {userCam}";
        var movement = moves.Count == 0 ? Field(cam, "movement_seen") : string.Join(" and ", moves.Select(CleanMove));
        if (mode == Cinematic && movement != null)
        {
            if (movement.Contains("handheld", StringComparison.OrdinalIgnoreCase)) movement = "smooth stabilized gimbal move following the subject";
            else if (movement.Contains("static", StringComparison.OrdinalIgnoreCase)) movement = "locked-off tripod shot with a very subtle slow push-in";
            else movement = $"smooth, steady {movement}";
        }
        return "Camera: " + Join(", ", size, angle != null ? $"{angle} angle".Replace("angle angle", "angle") : null, movement);
    }

    /// <summary>One step wider (close-up → medium, medium → medium-wide), never tighter than a medium shot.</summary>
    public static string Wider(string? size)
    {
        var x = (size ?? "").ToLowerInvariant();
        if (x.Contains("wide") || x.Contains("long") || x.Contains("establishing")) return size!;
        if (x.Contains("medium close") || x.Contains("medium-close")) return "medium-wide shot showing the person and the surroundings";
        if (x.Contains("close")) return "medium shot showing the person and the surroundings";
        return "medium-wide shot showing the person and the surroundings";
    }

    private static readonly (Regex When, string Rule)[] Rules =
    {
        (new(@"\b(fire|flames?|stove|hearth|embers?|coals?|campfire|bonfire|burning|firewood|smok)", RegexOptions.IgnoreCase),
            "Safety: hands stay well away from flames and hot coals, palms only held toward the warmth; nobody touches the fire, coals or a hot pot."),
        (new(@"\b(eat|eats|eating|chopsticks?|bowls?|rice|meal|chew|drink|drinks|drinking|spoon|food|noodles?)\b", RegexOptions.IgnoreCase),
            "Eating: small slow bites, chopsticks held correctly, food goes into the mouth, the bowl steady in the other hand."),
        (new(@"\b(walk|walks|walking|run|runs|running|steps?|stroll|carr(y|ies|ying))\b", RegexOptions.IgnoreCase),
            "Walking: natural gait, alternating legs, feet on the ground, unhurried pace."),
        (new(@"\b(knife|knives|cut|cuts|cutting|chop|chops|chopping|sickle|scissors|machete)\b", RegexOptions.IgnoreCase),
            "Tools: slow, careful, safe handling, the blade pointing away from hands and body."),
        (new(@"\b(river|pond|swim|swims|swimming|well|bath|bathing|wade|wading)\b", RegexOptions.IgnoreCase),
            "Water: calm, physically correct water and splashes; people stay safely in shallow water."),
        (new(@"\b(buffalo|cows?|dogs?|cats?|chickens?|hens?|ducks?|pigs?|goats?|horses?|birds?)\b", RegexOptions.IgnoreCase),
            "Animals: anatomically correct animals with the right number of legs, moving slowly and naturally."),
        (new(@"\b(child|children|kid|kids|boy|boys|girl|girls|baby|toddler)\b", RegexOptions.IgnoreCase),
            "Children: gentle, safe, age-appropriate behaviour."),
    };

    /// <summary>
    /// Bodies, motion and logic: the usual failures of video models (extra limbs, smeared motion, a child reaching into
    /// the fire) are named for what this scene shows — the rules fire on words of the analysis.
    /// </summary>
    public static List<string> Behaviour(JsonObject analysis, bool hasPeople)
    {
        var text = string.Join(" ", new[]
        {
            Field(analysis, "summary"), Field(analysis, "object_interaction"),
            string.Join(" ", Arr(analysis, "timeline").OfType<JsonObject>().Select(t => $"{Field(t, "action")} {Field(t, "body")}")),
            Field(Obj(analysis, "environment"), "location"), Obj(analysis, "environment")["objects"]?.ToJsonString()
        }.Where(x => x != null));
        var rules = new List<string>();
        if (hasPeople)
        {
            rules.Add("Anatomy: each person has one head, two arms, two five-fingered hands and two legs in natural proportions.");
            rules.Add("Motion: slow, simple, physically plausible movement, one action at a time; faces and hands stay sharp, no morphing.");
        }
        foreach (var (when, rule) in Rules)
            if (when.IsMatch(text) && (hasPeople || rule.StartsWith("Animals") || rule.StartsWith("Water"))) rules.Add(rule);
        return rules;
    }

    /// <summary>
    /// A handful of the theme's countryside details for this place (indoor or outdoor), the same for every scene of one
    /// environment of the bible — so the village looks rich but does not change from shot to shot.
    /// </summary>
    public static string RichDetails(AiStudioTopic? topic, JsonObject analysis, JsonObject? bibleEnv, int sceneIndex)
    {
        if (topic?.OutdoorDetails == null || topic.IndoorDetails == null) return "";
        // the bible's place decides (it is the same for every scene there); else what this scene's analysis saw
        var where = Field(bibleEnv, "location")
                    ?? string.Join(" ", Field(Obj(analysis, "environment"), "location"), Field(Obj(analysis, "environment"), "space"));
        var indoor = Regex.IsMatch(where, @"\b(indoor|inside|interior|kitchen|room|house|hut|home|bedroom|shed)\b", RegexOptions.IgnoreCase)
                     && !Regex.IsMatch(where, @"\b(outdoor|outside|yard|field|garden|road|path)\b", RegexOptions.IgnoreCase);
        var pool = indoor ? topic.IndoorDetails : topic.OutdoorDetails;
        var key = Field(bibleEnv, "id") ?? Field(bibleEnv, "location") ?? $"scene{sceneIndex}";
        uint h = 2166136261; // FNV-1a: stable across runs (string.GetHashCode is not)
        foreach (var ch in key.ToLowerInvariant()) { h ^= ch; h *= 16777619; }
        var picked = pool.Select((d, i) => (d, k: (h ^ (uint)(i * 2654435761u)) * 16777619u)).OrderBy(x => x.k).Take(5).Select(x => x.d);
        return string.Join(", ", picked);
    }

    private static string CleanMove(string m) => Regex.Replace(m, @"\s*\(.*?\)", "").Trim() switch
    {
        "static" => "static locked-off camera",
        "handheld" => "handheld camera with slight natural shake",
        var x => x
    };

    /// <summary>
    /// Fits the prompt under <paramref name="max"/>: the optional sentences go first (composition, depth, body
    /// mechanics, expression, lighting, look, the mild rules), then the setting and the narration are cut at a comma.
    /// The subject, the action, the camera and the safety / anatomy / continuity / quality lines are never cut.
    /// </summary>
    private static string Shorten(List<string> parts, int max)
    {
        var order = new[] { "Composition:", "Body mechanics:", "Expression:", "Lighting:", "Children:", "Animals:", "Water:", "Walking:", "Depth of field:", "Look:" };
        var list = parts.ToList();
        int Length() => string.Join(" ", list).Length;
        foreach (var prefix in order)
        {
            if (Length() <= max) break;
            list.RemoveAll(p => p.StartsWith(prefix, StringComparison.Ordinal));
        }
        foreach (var prefix in new[] { "Setting:", "Narration over this scene", "Color:", "Action:" })
        {
            if (Length() <= max) break;
            var i = list.FindIndex(p => p.StartsWith(prefix, StringComparison.Ordinal));
            if (i < 0) continue;
            var room = list[i].Length - (Length() - max);
            list[i] = CutAtComma(list[i], Math.Max(prefix.Length + 40, room));
        }
        var text = string.Join(" ", list);
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    private static string CutAtComma(string sentence, int max)
    {
        if (sentence.Length <= max) return sentence;
        var cut = sentence.LastIndexOf(", ", Math.Max(0, max - 1), StringComparison.Ordinal);
        var kept = (cut > 0 ? sentence[..cut] : sentence[..Math.Max(0, max - 1)]).TrimEnd(',', ' ');
        if (kept.Count(c => c == '"') % 2 == 1) kept += "\"";
        return kept + ".";
    }

    // ───────────── helpers ─────────────

    /// <summary>A usable fact: not empty, not "uncertain" / "none" / "n/a".</summary>
    public static string? Field(JsonObject? o, string name)
    {
        if (o == null || !o.TryGetPropertyValue(name, out var node) || node == null) return null;
        return Clean(node is JsonValue ? node.ToString() : node.ToJsonString());
    }

    public static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = Regex.Replace(s.Trim(), @"\s+", " ").Trim().TrimEnd('.', ';', ',').Trim();
        if (t.Length == 0) return null;
        var lower = t.ToLowerInvariant();
        if (lower is "uncertain" or "unknown" or "none" or "n/a" or "na" or "not visible" or "not applicable" or "-" or "null") return null;
        if (lower.StartsWith("uncertain")) return null;
        return t;
    }

    private static JsonObject Obj(JsonObject? o, string name) => o?[name] as JsonObject ?? new JsonObject();
    private static JsonArray Arr(JsonObject? o, string name) => o?[name] as JsonArray ?? new JsonArray();

    private static double? Num(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    /// <summary>Joins the usable items, dropping comma-separated phrases already said ("dirt ground, bamboo wall" twice → once).</summary>
    private static string Join(string sep, params string?[] items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();
        foreach (var item in items.Select(Clean).Where(x => x != null))
        {
            var phrases = item!.Split(", ", StringSplitOptions.RemoveEmptyEntries)
                .Select(ph => ph.Trim().TrimEnd('.'))
                .Where(ph => ph.Length > 0 && seen.Add(Regex.Replace(ph.ToLowerInvariant(), @"[^\p{L}\p{N} ]", "").Trim()))
                .ToList();
            if (phrases.Count > 0) kept.Add(string.Join(", ", phrases));
        }
        return string.Join(sep, kept);
    }

    private static void AddIf(List<string> parts, string? s)
    {
        if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
    }

    private static string Sentence(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return s;
        return s.EndsWith('.') || s.EndsWith('!') || s.EndsWith('?') ? s : s + ".";
    }

    private static double Round(double x) => Math.Round(x * 2, MidpointRounding.AwayFromZero) / 2.0;
    private static string Fmt(double x) => x.ToString(x % 1 == 0 ? "0" : "0.0", Inv);

    /// <summary>Identity guard: drops "named X" / "@handle" fragments the analysis may contain.</summary>
    private static string StripNames(string s)
    {
        s = Regex.Replace(s, @"@\w+", "a person");
        s = Regex.Replace(s, @"\b(named|called)\s+[A-Z][\w-]*(\s+[A-Z][\w-]*)?", "", RegexOptions.None);
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }
}
