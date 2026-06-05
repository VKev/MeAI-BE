using Infrastructure.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Logic.SocialMedia;

public sealed class SocialMediaPostSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SocialMediaPostSyncOptions> _options;
    private readonly ILogger<SocialMediaPostSyncWorker> _logger;

    public SocialMediaPostSyncWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SocialMediaPostSyncOptions> options,
        ILogger<SocialMediaPostSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialDelay = GetInitialDelay();
        if (initialDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(initialDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await QueueSyncsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Recurring social media post sync iteration failed.");
            }

            try
            {
                await Task.Delay(GetInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task QueueSyncsAsync(CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<SocialMediaPostSyncDispatcher>();
        await dispatcher.QueueRecurringSyncsAsync(cancellationToken);
    }

    private TimeSpan GetInitialDelay()
    {
        var seconds = _options.CurrentValue.InitialDelaySeconds;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 300));
    }

    private TimeSpan GetInterval()
    {
        var seconds = _options.CurrentValue.IntervalSeconds;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 10, 3600));
    }
}
