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
        _logger.LogInformation("NewsStudio.Worker is starting.");
        
        // Ensure temporary storage directory exists
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

        // Register graceful shutdown actions
        _lifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("Worker received SIGTERM/Stop signal. Initiating graceful shutdown...");
        });

        _lifetime.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("Worker stopped gracefully.");
        });

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker loop started at: {time}", DateTimeOffset.Now);

        // In Phase 1, the worker runs its lifecycle loop.
        // In Phase 2, Hangfire Server will be hosted here to process asynchronous jobs.
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("NewsStudio.Worker heartbeat active: {time}", DateTimeOffset.Now);

            try
            {
                // Heartbeat interval 30 seconds
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker cancellation requested.");
                break;
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("NewsStudio.Worker stopping.");
        return base.StopAsync(cancellationToken);
    }
}
