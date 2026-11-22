namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// Fixed 30-second two-character test scene used to judge the GPU pipeline (film_bench.py test "D"):
/// shot / reverse shot, speakers and silent reactions. The lines are fixed so the public voice endpoint can only
/// ever synthesise these (cached after the first call), never arbitrary text on the Gemini key.
/// </summary>
public static class FilmTestScene
{
    public const string WomanVoice = "gemini:Kore";
    public const string ManVoice = "gemini:Orus";

    public static readonly IReadOnlyList<(string Voice, string Text)> Lines = new[]
    {
        (WomanVoice, "Anh lại quên rồi, đúng không? Hôm nay là sinh nhật con."),
        (ManVoice, "Anh xin lỗi... cuộc họp kéo dài, anh không về kịp."),
        (WomanVoice, "Lần nào anh cũng có lý do. Con chờ anh cả buổi tối."),
        (ManVoice, "Anh biết. Anh về ngay bây giờ, anh sẽ bù cho con."),
        (WomanVoice, "Về đi. Con vẫn để dành cho anh một miếng bánh."),
    };
}
