using Microsoft.Extensions.Options;
using TSIC.API.Configuration;
using TSIC.API.Extensions;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Sweep;

/// <summary>
/// Daily timer that runs the ADN sweep at the configured local hour (default 4 AM).
/// Mirrors legacy AdnArbSweepService.ExecuteAsync exactly: initial delay to next run,
/// then PeriodicTimer 24h loop. Skips entirely if disabled.
/// </summary>
public sealed class AdnSweepBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly AdnSweepOptions _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<AdnSweepBackgroundService> _logger;

    public AdnSweepBackgroundService(
        IServiceProvider services,
        IOptions<AdnSweepOptions> options,
        IHostEnvironment env,
        ILogger<AdnSweepBackgroundService> logger)
    {
        _services = services;
        _options = options.Value;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hard guard: this sweep walks settled batches in REAL prod authorize.net. Only TSIC-PHOENIX
        // may run it. Any other host (dev box / Staging, local, CI) self-disables regardless of
        // ASPNETCORE_ENVIRONMENT or AdnSweepOptions.Enabled.
        if (!_env.IsLiveProduction())
        {
            _logger.LogInformation(
                "AdnSweepBackgroundService skipped: host is not TSIC-PHOENIX (machine={MachineName}, env={EnvName}).",
                System.Environment.MachineName,
                _env.EnvironmentName);
            return;
        }

        if (!_options.Enabled)
        {
            _logger.LogInformation("AdnSweepBackgroundService disabled via config; exiting.");
            return;
        }

#if DEBUG
        _logger.LogInformation("AdnSweepBackgroundService idle in DEBUG (use POST /api/admin/adn-sweep/run for manual trigger).");
        return;
#else
        _logger.LogInformation("AdnSweepBackgroundService starting; daily run at hour {Hour} local",
            _options.SweepHourLocal);

        try
        {
            // Calc initial delay to next sweep hour, with grace window for late starts.
            var now = DateTime.Now;
            var nextRun = now.Date.AddHours(_options.SweepHourLocal);
            if (now > nextRun.AddMinutes(_options.StartupGraceMinutes))
            {
                nextRun = nextRun.AddDays(1);
            }
            var initialDelay = nextRun - now;
            if (initialDelay < TimeSpan.Zero) initialDelay = TimeSpan.Zero;

            _logger.LogInformation("First sweep at {NextRun:g} (in {Hours:F1}h)", nextRun, initialDelay.TotalHours);
            await Task.Delay(initialDelay, stoppingToken);

            await RunOnceAsync(stoppingToken);
            // await RunOnceAsync(stoppingToken, customerOverride: TripleThreatCustomerId);

            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
                // await RunOnceAsync(stoppingToken, customerOverride: TripleThreatCustomerId);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AdnSweepBackgroundService stopping.");
        }
#endif
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var sweep = scope.ServiceProvider.GetRequiredService<IAdnSweepService>();
            var result = await sweep.RunAsync("Scheduled", _options.DaysPriorWindow, ct);
            _logger.LogInformation(
                "Sweep finished: checked={Checked} arbImported={ArbImported} ecReturns={EcReturns} errored={Errored}",
                result.Checked, result.ArbImported, result.EcheckReturnsProcessed, result.Errored);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sweep tick threw; will retry on next 24h tick");
        }
    }
}
