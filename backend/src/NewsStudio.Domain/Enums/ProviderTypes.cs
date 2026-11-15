namespace NewsStudio.Domain.Enums;

public enum AiProviderType
{
    Gemini = 1,
    Claude = 2
}

public enum TtsProviderType
{
    EdgeTts = 1,
    ElevenLabs = 2,
    Azure = 3,
    GoogleTts = 4,
    Custom = 5
}

public enum ImageCategory
{
    Panorama = 1,
    Character = 2,
    Event = 3,
    Document = 4,
    Chart = 5,
    Unsuitable = 6
}
