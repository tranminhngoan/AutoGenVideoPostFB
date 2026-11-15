namespace NewsStudio.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _lifetime;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _configuration = configuration;
        _lifetime = lifetime;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("NewsStudio.Worker is starting with Hangfire Background Server active.");

        var tempStoragePath = _configuration["Storage:TempStoragePath"] ?? "./tmp/newsstudio";
        try
        {
            if (!Directory.Exists(tempStoragePath))
            {
                Directory.CreateDirectory(tempStoragePath);
                _logger.LogInformation("Created temporary storage directory at: {Path}", tempStoragePath);
            }
            else
            {
                _logger.LogInformation("Temporary storage directory verified at: {Path}", tempStoragePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize temp storage directory: {Path}", tempStoragePath);
        }

        _lifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("Worker received SIGTERM/Stop signal. Gracefully waiting for in-flight Hangfire jobs to complete or pause...");
        });

        _lifetime.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("Worker stopped gracefully.");
        });

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker service loop is running. Awaiting jobs from Hangfire queues.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Heartbeat every 60 seconds
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                _logger.LogDebug("NewsStudio.Worker heartbeat: ready for tasks.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker execution cancellation triggered.");
                break;
            }
        }
    }
}
