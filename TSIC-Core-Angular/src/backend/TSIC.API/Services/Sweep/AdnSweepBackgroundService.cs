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
        // On the 1st the sweep's digest is folded into the month-end close email instead of being mailed
        // on its own — one message, sweep on top, close below. Every other morning it mails itself.
        var isMonthEnd = DateTime.Now.Day == 1 && _options.EmailMonthEndClose;

        AdnSweepResult? result = null;
        try
        {
            using var scope = _services.CreateScope();
            var sweep = scope.ServiceProvider.GetRequiredService<IAdnSweepService>();
            result = await sweep.RunAsync("Scheduled", _options.DaysPriorWindow, sendDigest: !isMonthEnd, ct);
            _logger.LogInformation(
                "Sweep finished: succeeded={Succeeded} checked={Checked} arbImported={ArbImported} ecReturns={EcReturns} orphansFound={OrphansFound} errored={Errored}",
                result.Succeeded, result.Checked, result.ArbImported, result.EcheckReturnsProcessed, result.OrphansFound, result.Errored);
        }
        catch (Exception ex)
        {
            // RunAsync catches its own failures and reports them on the result, so reaching here means
            // something outside the sweep proper broke (DI, SweepLog). result stays null.
            _logger.LogError(ex, "Sweep tick threw; will retry on next 24h tick");
        }

        if (!isMonthEnd) return;

        // The sweep suppressed its digest for us — we now OWN that send. Every path below must mail it,
        // or the morning goes silent on the one day that matters most.
        await RunMonthEndCloseAsync(result, ct);
    }

    private async Task RunMonthEndCloseAsync(AdnSweepResult? sweep, CancellationToken ct)
    {
        var lastMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1);
        try
        {
            _logger.LogInformation("Month-end close starting for {Month:MMMM yyyy}", lastMonth);

            using var scope = _services.CreateScope();
            var reconciliation = scope.ServiceProvider.GetRequiredService<IAdnReconciliationService>();

            // A null sweep means the sweep didn't even return a result — treat that as untrustworthy, not
            // as "no sweep ran", so the close withholds the files rather than shipping a ledger built on
            // books the sweep may never have written.
            var gate = sweep ?? UnknownSweep();
            await reconciliation.EmailMonthlyCloseAsync(lastMonth.Month, lastMonth.Year, gate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Month-end close for {Month:MMMM yyyy} failed; falling back to the sweep digest alone", lastMonth);

            // The close blew up but we still hold the day's suppressed digest. Mail it, with the failure
            // noted — losing the daily sweep report because the monthly close crashed is not acceptable.
            await SendDigestFallbackAsync(sweep, lastMonth, ex, ct);
        }
    }

    /// <summary>Stand-in for a sweep that never reported: fails the trust gate, so no files ship.</summary>
    private static AdnSweepResult UnknownSweep() => new()
    {
        Checked = 0,
        ArbImported = 0,
        EcheckSettled = 0,
        EcheckReturnsProcessed = 0,
        OrphansFound = 0,
        Errored = 0,
        Succeeded = false,
        ErrorMessage = "The sweep did not return a result — it failed before reporting. Nothing about this "
            + "morning's booking can be assumed.",
    };

    private async Task SendDigestFallbackAsync(
        AdnSweepResult? sweep, DateTime lastMonth, Exception closeFailure, CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var body =
                "<p style='font-size:13px;color:#b00;font-weight:bold;'>&#9888; The month-end close for "
                + $"{lastMonth:MMMM yyyy} failed to run. No QuickBooks files were produced. Run it by hand from "
                + "Accounting &rarr; ADN End-of-Month / IIF once the cause is fixed.</p>"
                + $"<p style='font-size:11px;color:#b00;'><b>Close error:</b> {closeFailure.Message}</p>"
                + "<hr style='margin:20px 0;border:0;border-top:1px solid #ccc;' />"
                + (sweep?.DigestHtml ?? "<p style='font-size:11px;'>The sweep produced no digest either.</p>");

            await email.SendAsync(new EmailMessageDto
            {
                FromName = "TSIC System",
                ToAddresses = [TsicConstants.SupportEmail],
                Subject = $"AdnSweep — {lastMonth:MMMM yyyy} MONTH-END CLOSE FAILED",
                HtmlBody = body,
            }, sendInDevelopment: true, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Month-end fallback digest send failed — the morning report is LOST");
        }
    }
#endif
}
