using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

public interface ISystemHealthService
{
    Task<SystemHealthDto> CheckHealthAsync(CancellationToken cancellationToken = default);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
    Task<bool> IsLiveAsync(CancellationToken cancellationToken = default);
}
