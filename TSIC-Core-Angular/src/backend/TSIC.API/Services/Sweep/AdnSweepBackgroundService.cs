using Microsoft.Extensions.Options;
using TSIC.API.Configuration;
using TSIC.API.Extensions;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

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
        using var scope = _services.CreateScope();

        // On the 1st the whole morning is one flow — sweep, then close, then ONE email with the digest
        // folded in and the .zip attached if the sweep can be trusted. That flow lives in exactly one
        // place, RunMonthEndCloseWithSweepAsync, which is also what the email-close endpoint calls with
        // includeSweep=true. So what gets tested is this, not a reconstruction of this.
        if (DateTime.Now.Day == 1 && _options.EmailMonthEndClose)
        {
            var lastMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1);
            _logger.LogInformation("Month-end close starting for {Month:MMMM yyyy}", lastMonth);

            try
            {
                var reconciliation = scope.ServiceProvider.GetRequiredService<IAdnReconciliationService>();
                // "Scheduled" — the 1st-of-month run IS the scheduled sweep, just with its digest folded
                // into the close email. echeck.SweepLog's CHECK constraint permits only Scheduled/Manual.
                var close = await reconciliation.RunMonthEndCloseWithSweepAsync(
                    lastMonth.Month, lastMonth.Year, "Scheduled", ct);

                _logger.LogInformation(
                    "Month-end close finished for {Month:MMMM yyyy}: sweepSucceeded={Succeeded} sweepErrored={Errored} filesAttached={Attached} closeError={CloseError}",
                    lastMonth, close.Sweep.Succeeded, close.Sweep.Errored, close.FilesAttached, close.CloseError);
            }
            catch (Exception ex)
            {
                // RunMonthEndCloseWithSweepAsync is written not to throw — it mails the digest even when
                // the close dies. Reaching here means DI or the mail path itself failed, and the morning
                // report is lost; that is what this log line is for.
                _logger.LogError(ex, "Month-end close threw out of its own guard for {Month:MMMM yyyy}", lastMonth);
            }
            return;
        }

        // Every other morning: the plain sweep, mailing its own digest.
        try
        {
            var sweep = scope.ServiceProvider.GetRequiredService<IAdnSweepService>();
            var result = await sweep.RunAsync("Scheduled", _options.DaysPriorWindow, sendDigest: true, ct);
            _logger.LogInformation(
                "Sweep finished: succeeded={Succeeded} checked={Checked} arbImported={ArbImported} ecReturns={EcReturns} orphansFound={OrphansFound} errored={Errored}",
                result.Succeeded, result.Checked, result.ArbImported, result.EcheckReturnsProcessed, result.OrphansFound, result.Errored);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sweep tick threw; will retry on next 24h tick");
        }

        // On the 2nd and the 15th, follow the sweep with the expiring-card pass. NOT the 1st: that
        // morning belongs entirely to the month-end close, and a second unattended send behind the
        // close's IsTrustworthy gate would be invisible whenever the gate held. Deliberately AFTER the
        // sweep, and in its own try, for the same reason step 8 is last inside the sweep -- proven
        // money code is scored before any of this runs, and nothing here can change its verdict.
        //
        // TWICE A MONTH IS DELIBERATE, and there is deliberately no dedupe between the two dates.
        // Todd's ruling, 2026-08-25. ARBGetSubscriptionList(cardExpiringThisMonth) matches on the card
        // CURRENTLY attached to the subscription, so a family who updated theirs after the 2nd drops
        // out of the 15th's result set on its own. The second pass therefore reaches only the families
        // who did nothing -- a follow-up to a non-responder, not a duplicate. Suppressing it by
        // matching emailLogs would withhold the reminder from exactly the people who still need it.
        // Do not "fix" this by adding dedupe or by dropping one of the dates.
        if (DateTime.Now.Day is 2 or 15)
        {
            try
            {
                var notify = scope.ServiceProvider.GetRequiredService<IArbNotificationService>();
                var expiring = await notify.NotifyExpiringCardsAsync(ct);
                _logger.LogInformation(
                    "Expiring-card pass finished: found={Found} emailed={Emailed} notEmailed={NotEmailed}",
                    expiring.Found, expiring.Emailed, expiring.Skipped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expiring-card pass threw; sweep results are unaffected");
            }
        }
    }
#endif
}
