namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// Fixed test scene used to judge the GPU pipeline (film_bench.py test "E"): the user's Mekong-countryside script —
/// Anh Hai grilling snakehead fish calls Út Tèo off his motorbike. Each spoken line is split at its sentence break so
/// every shot stays within Wan's ~5 s clip. The lines are fixed so the public voice endpoint can only ever synthesise
/// these (cached after the first call), never arbitrary text on the Gemini key.
/// </summary>
public static class FilmTestScene
{
    public const string AnhHaiVoice = "gemini:Algenib"; // gravelly, older — "giọng rổn rảng"
    public const string UtTeoVoice = "gemini:Puck";     // lively, quick

    public static readonly IReadOnlyList<(string Voice, string Text)> Lines = new[]
    {
        (AnhHaiVoice, "Ê Út Tèo! Đi đâu mà chạy vù vù dữ thần ôn vậy mậy?"),
        (AnhHaiVoice, "Tấp vô đây làm vài ly coi!"),
        (UtTeoVoice, "Thôi anh Hai ơi, em đang gấp ra chợ rước bà xã."),
        (UtTeoVoice, "Mà anh nướng cái gì thơm lừng cả xóm vậy trời?"),
        (AnhHaiVoice, "Cá lóc đồng mới câu hồi sáng, mập ú nu hà!"),
        (AnhHaiVoice, "Mắm me bằm ớt xong hết trơn rồi, thiếu mỗi bạn hiền thôi đó!"),
        (UtTeoVoice, "Trời đất ơi, nói sớm chút đỡ tốn công em ngập ngừng!"),
        (UtTeoVoice, "Anh lấy giùm em cái chén, để em điện cho bả báo... xe lủng bánh cái nha!"),
        (AnhHaiVoice, "Ha ha ha! Cái thằng quỷ này!"),
    };
}
