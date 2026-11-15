namespace NewsStudio.Application.DTOs;

public class SystemHealthDto
{
    public string Status { get; set; } = "Healthy";
    public string Environment { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
    public IDictionary<string, ComponentHealthDto> Components { get; set; } = new Dictionary<string, ComponentHealthDto>();
}

public class ComponentHealthDto
{
    public string Status { get; set; } = "Healthy";
    public string? Description { get; set; }
    public TimeSpan Duration { get; set; }
}
