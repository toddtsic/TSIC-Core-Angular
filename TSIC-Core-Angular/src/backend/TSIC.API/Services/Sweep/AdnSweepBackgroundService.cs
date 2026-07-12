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
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "AdnSweepBackgroundService stopping.");
        }
#endif
    }

#if !DEBUG
    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var sweep = scope.ServiceProvider.GetRequiredService<IAdnSweepService>();
            var result = await sweep.RunAsync("Scheduled", _options.DaysPriorWindow, ct);
            _logger.LogInformation(
                "Sweep finished: checked={Checked} arbImported={ArbImported} ecReturns={EcReturns} orphansFound={OrphansFound} errored={Errored}",
                result.Checked, result.ArbImported, result.EcheckReturnsProcessed, result.OrphansFound, result.Errored);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sweep tick threw; will retry on next 24h tick");
        }

        // On the 1st, the month just ended is complete — close it and mail the QuickBooks files, so the
        // operator wakes up to the .iif instead of driving the wizard by hand. Runs AFTER the daily sweep
        // (which may still be booking ARB/eCheck rows into the closing month) and in its own scope + try,
        // so a close failure never takes down the sweep loop or the next tick.
        if (DateTime.Now.Day == 1 && _options.EmailMonthEndClose)
        {
            await RunMonthEndCloseAsync(ct);
        }
    }

    private async Task RunMonthEndCloseAsync(CancellationToken ct)
    {
        var lastMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1);
        try
        {
            _logger.LogInformation("Month-end close starting for {Month:MMMM yyyy}", lastMonth);

            using var scope = _services.CreateScope();
            var reconciliation = scope.ServiceProvider.GetRequiredService<IAdnReconciliationService>();
            await reconciliation.EmailMonthlyCloseAsync(lastMonth.Month, lastMonth.Year, ct);
        }
        catch (Exception ex)
        {
            // No retry: the operator can always run the close by hand from Accounting → ADN End-of-Month.
            _logger.LogError(ex, "Month-end close for {Month:MMMM yyyy} failed; run it by hand from the UI", lastMonth);
        }
    }
#endif
}
