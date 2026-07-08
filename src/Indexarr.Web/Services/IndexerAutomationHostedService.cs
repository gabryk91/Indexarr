using Indexarr.Web.Options;
using Microsoft.Extensions.Options;

namespace Indexarr.Web.Services;

public sealed class IndexerAutomationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<IndexarrOptions> _options;
    private readonly ILogger<IndexerAutomationHostedService> _logger;
    private readonly AutomationRuntimeState _runtimeState;

    public IndexerAutomationHostedService(IServiceScopeFactory scopeFactory, IOptions<IndexarrOptions> options, ILogger<IndexerAutomationHostedService> logger, AutomationRuntimeState runtimeState)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _runtimeState = runtimeState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupPending = true;
        DateTimeOffset? nextScheduledAtUtc = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var configuration = await LoadAutomationConfigurationAsync(stoppingToken);
            _runtimeState.Configure(configuration.Enabled, configuration.IntervalMinutes);

            if (!configuration.Enabled)
            {
                startupPending = true;
                nextScheduledAtUtc = null;
                _runtimeState.SetNextScheduled(null);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                continue;
            }

            var interval = TimeSpan.FromMinutes(configuration.IntervalMinutes);

            if (startupPending)
            {
                await RunScopedAsync("startup", stoppingToken);
                startupPending = false;
                nextScheduledAtUtc = DateTimeOffset.UtcNow.Add(interval);
                _runtimeState.SetNextScheduled(nextScheduledAtUtc);
            }
            else if (!nextScheduledAtUtc.HasValue)
            {
                nextScheduledAtUtc = DateTimeOffset.UtcNow.Add(interval);
                _runtimeState.SetNextScheduled(nextScheduledAtUtc);
            }
            else if (DateTimeOffset.UtcNow >= nextScheduledAtUtc.Value)
            {
                await RunScopedAsync("scheduled", stoppingToken);
                nextScheduledAtUtc = DateTimeOffset.UtcNow.Add(interval);
                _runtimeState.SetNextScheduled(nextScheduledAtUtc);
            }

            var delay = nextScheduledAtUtc.HasValue
                ? nextScheduledAtUtc.Value - DateTimeOffset.UtcNow
                : TimeSpan.FromSeconds(15);

            if (delay < TimeSpan.FromSeconds(1))
            {
                delay = TimeSpan.FromSeconds(1);
            }

            if (delay > TimeSpan.FromSeconds(15))
            {
                delay = TimeSpan.FromSeconds(15);
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<(bool Enabled, int IntervalMinutes)> LoadAutomationConfigurationAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var configurationService = scope.ServiceProvider.GetRequiredService<AppConfigurationService>();
        var configuration = await configurationService.GetAsync(cancellationToken);
        var fallback = _options.Value.Automation;

        return (
            configuration?.AutomationEnabled ?? fallback.Enabled,
            Math.Max(1, configuration?.AutomationIntervalMinutes ?? fallback.IntervalMinutes)
        );
    }

    private async Task RunScopedAsync(string trigger, CancellationToken cancellationToken)
    {
        _runtimeState.Start(trigger);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IndexerAutomationService>();
            var result = await service.RunHealthChecksAsync(trigger, cancellationToken);
            _runtimeState.Complete(result.Reachable, result.Reachable
                ? $"{trigger}: {result.HealthyIndexers} OK, {result.FailedIndexers} KO."
                : $"{trigger}: {result.ErrorMessage}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _runtimeState.Complete(false, $"{trigger}: annullata.");
        }
        catch (Exception ex)
        {
            _runtimeState.Complete(false, $"{trigger}: {ex.Message}");
            _logger.LogWarning(ex, "Scheduled automation run failed.");
        }
    }
}
