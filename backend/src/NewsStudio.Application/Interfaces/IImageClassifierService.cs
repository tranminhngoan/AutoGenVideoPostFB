using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.Interfaces;

public interface IImageClassifierService
{
    ImageCategory ClassifyImage(string? caption, string? altText, string articleContext);
}
