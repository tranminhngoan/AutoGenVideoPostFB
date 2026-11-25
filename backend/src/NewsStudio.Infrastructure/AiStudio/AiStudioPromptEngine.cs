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
    string NegativePrompt);

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
            var descs = cast.Select(c => DescribeCharacter(c, creative ? Clean(v.Wardrobe) : null)).Where(d => d.Length > 0).ToList();
            if (descs.Count > 0) parts.Add(Sentence((descs.Count == 1 ? "Subject: " : $"Subjects ({descs.Count}): ") + string.Join("; ", descs)));
        }
        else if (people.Count > 0)
        {
            var descs = people.OfType<JsonObject>().Select(c => DescribeCharacter(c, creative ? Clean(v.Wardrobe) : null)).Where(d => d.Length > 0).ToList();
            if (descs.Count > 0) parts.Add(Sentence((descs.Count == 1 ? "Subject: " : $"Subjects ({descs.Count}): ") + string.Join("; ", descs)));
        }

        // 3. environment
        if (creative && Clean(v.Setting) is { } setting) parts.Add(Sentence($"Setting: {setting}"));
        else AddIf(parts, DescribeEnvironment(a, EnvironmentOf(s)) is { Length: > 0 } env ? Sentence($"Setting: {env}") : null);

        // 4–5. main action + timed sequence (scaled to the clip length)
        AddIf(parts, Field(a, "summary") is { } summary ? Sentence($"Action: {StripNames(summary)}") : null);
        var seq = Sequence(s);
        if (seq.Length > 0) parts.Add(Sentence($"Sequence: {seq}"));

        // 6. body mechanics + motion quality
        var body = Obj(a, "body_movement");
        var mechanics = Join(", ", Field(body, "coordination"), Field(body, "torso"), Field(body, "legs"));
        var mq = Obj(a, "motion_quality");
        var physics = Join(", ", Field(mq, "naturalness"), Field(mq, "inertia"), Field(mq, "balance"),
            Field(mq, "cloth") is { } cloth ? $"cloth {cloth}" : null, Field(mq, "hair") is { } hair ? $"hair {hair}" : null);
        if (mechanics.Length > 0 || physics.Length > 0)
            parts.Add(Sentence("Body mechanics: " + Join("; ", mechanics, physics) + (cinematic ? "; real weight, momentum and follow-through" : "")));

        // 7. facial expression
        var face = Obj(a, "facial_expression");
        AddIf(parts, Join(", ", Field(face, "expression"), Field(face, "emotion"), Field(face, "eyes"), Field(face, "changes")) is { Length: > 0 } f
            ? Sentence($"Expression: {f}") : null);

        // 8–9. camera angle, shot size, movement (measured)
        parts.Add(Sentence(Camera(s, mode, v)));

        // 10. composition
        var comp = Obj(a, "composition");
        var composition = Join(", ", Field(comp, "subject_placement"), Field(comp, "rule_of_thirds"), Field(comp, "leading_lines"),
            Field(comp, "depth"), Field(comp, "negative_space"));
        if (cinematic) composition = Join(", ", composition, "balanced rule-of-thirds framing with clear foreground, midground and background layers");
        AddIf(parts, composition.Length > 0 ? Sentence($"Composition: {composition}") : null);

        // 11. lighting
        var light = Obj(a, "lighting");
        string lighting;
        if (creative && Clean(v.Lighting) is { } userLight) lighting = userLight;
        else
        {
            lighting = Join(", ", Field(light, "source"), Field(light, "direction") is { } d ? $"from {d}" : null, Field(light, "intensity"),
                Field(light, "color_temperature") is { } ct ? $"{ct} color temperature" : null, Field(light, "shadows") is { } sh ? $"{sh} shadows" : null);
            if (cinematic) lighting = Join(", ", lighting, "soft motivated key light with gentle rim light separating the subject");
        }
        AddIf(parts, lighting.Length > 0 ? Sentence($"Lighting: {lighting}") : null);

        // 12. colour
        var color = Obj(a, "color");
        var measuredTone = Field(s.Measured, "tone");
        var colour = Join(", ", Field(color, "dominant"), Field(color, "tone") ?? measuredTone, Field(color, "saturation") is { } sat ? $"{sat} saturation" : null,
            Field(color, "contrast") is { } con ? $"{con} contrast" : null, Field(color, "grading"));
        if (cinematic) colour = Join(", ", colour, "cinematic color grade with rich filmic contrast, natural skin tones");
        if (creative && Clean(v.Style) is { } userStyle) colour = Join(", ", colour, userStyle);
        AddIf(parts, colour.Length > 0 ? Sentence($"Color: {colour}") : null);

        // 13. depth of field
        var dof = Obj(a, "depth_of_field");
        var depth = Join(", ", Field(dof, "focus_subject") is { } fs ? $"focus on {fs}" : null, Field(dof, "background_blur") is { } bb ? $"background {bb}" : null,
            Field(dof, "focus_change"));
        if (cinematic) depth = Join(", ", depth, "shallow depth of field with smooth natural bokeh");
        AddIf(parts, depth.Length > 0 ? Sentence($"Depth of field: {depth}") : null);

        // 14. physical realism
        var real = Obj(a, "realism");
        var realism = Join(", ", Field(real, "skin") is { } sk ? $"skin {sk}" : null, Field(real, "fabric") is { } fb ? $"fabric {fb}" : null,
            Field(real, "surfaces"), Field(real, "shadows") is { } rs ? $"shadows {rs}" : null, Field(real, "physics"));
        AddIf(parts, realism.Length > 0 ? Sentence($"Realism: {realism}") : null);

        // 15. continuity
        var continuity = s.IsContinuation
            ? "Continuity: this continues the previous shot without a cut — same person, outfit, place, light and camera position, motion picks up exactly where it stopped"
            : s.HasPreviousScene
                ? "Continuity: same characters, outfits and look as the other scenes; one continuous take, smooth natural motion"
                : "Continuity: one continuous take, smooth natural motion";
        parts.Add(Sentence(continuity));

        // 16. visual quality
        parts.Add(Sentence(cinematic
            ? "Quality: photorealistic cinematic live-action footage, high detail, fine skin and fabric texture, stable exposure, no artifacts"
            : "Quality: photorealistic live-action footage, natural detail and texture, stable exposure, no artifacts"));

        // 17. constraints (Seedance has no negative prompt field)
        var negative = Clean(s.NegativePrompt) ?? DefaultNegative;
        var prompt = string.Join(" ", parts);
        var avoid = $" Avoid: {negative.TrimEnd('.')}.";
        if (prompt.Length + avoid.Length > MaxPromptChars) prompt = Shorten(parts, MaxPromptChars - avoid.Length);
        return (prompt + avoid).Trim();
    }

    // ───────────── pieces ─────────────

    /// <summary>The bible characters of this scene (by their scene numbers).</summary>
    private static List<JsonObject> CastOf(ScenePromptInput s) =>
        s.Characters.OfType<JsonObject>().Where(c => InScene(c, s.Index)).ToList();

    private static JsonObject? EnvironmentOf(ScenePromptInput s) =>
        s.Environments.OfType<JsonObject>().FirstOrDefault(e => InScene(e, s.Index));

    private static bool InScene(JsonObject o, int index) =>
        o["scenes"] is JsonArray arr && arr.Any(x => x is JsonValue jv && jv.TryGetValue<double>(out var n) && (int)n == index);

    private static string DescribeCharacter(JsonObject c, string? wardrobeOverride)
    {
        var label = Field(c, "label");
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

    /// <summary>"0–2s: … ; 2–4s: …" with the reference times mapped onto the clip length.</summary>
    public static string Sequence(ScenePromptInput s)
    {
        var tl = Arr(s.Analysis, "timeline").OfType<JsonObject>().ToList();
        if (tl.Count == 0) return string.Empty;
        var srcDur = Math.Max(0.1, s.EndSeconds - s.StartSeconds);
        var scale = s.TargetSeconds / srcDur;
        var items = new List<string>();
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
            var from = Round((a - s.StartSeconds) * scale);
            var to = Round((b - s.StartSeconds) * scale);
            if (to <= from) to = Math.Min(s.TargetSeconds, from + 0.5);
            items.Add($"{Fmt(from)}–{Fmt(to)}s {StripNames(text)}");
        }
        return string.Join("; ", items);
    }

    private static string Camera(ScenePromptInput s, string mode, AiStudioVariationDto v)
    {
        var cam = Obj(s.Analysis, "camera");
        var angle = Field(cam, "angle");
        var size = Field(cam, "shot_size");
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

    private static string CleanMove(string m) => Regex.Replace(m, @"\s*\(.*?\)", "").Trim() switch
    {
        "static" => "static locked-off camera",
        "handheld" => "handheld camera with slight natural shake",
        var x => x
    };

    private static string Shorten(List<string> parts, int max)
    {
        // drop the least important sentences first (realism, composition, depth of field), then cut
        var order = new[] { "Realism:", "Composition:", "Depth of field:", "Expression:", "Body mechanics:" };
        var list = parts.ToList();
        foreach (var prefix in order)
        {
            if (string.Join(" ", list).Length <= max) break;
            list.RemoveAll(p => p.StartsWith(prefix, StringComparison.Ordinal));
        }
        var text = string.Join(" ", list);
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
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
