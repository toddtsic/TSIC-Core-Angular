using Microsoft.Extensions.Options;
using TSIC.API.Configuration;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Echeck;

public sealed class EcheckSweepBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly EcheckSweepOptions _options;
    private readonly ILogger<EcheckSweepBackgroundService> _logger;

    public EcheckSweepBackgroundService(
        IServiceProvider services,
        IOptions<EcheckSweepOptions> options,
        ILogger<EcheckSweepBackgroundService> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("eCheck sweep BackgroundService is disabled via config; exiting.");
            return;
        }

        _logger.LogInformation("eCheck sweep BackgroundService starting; daily sweep at hour {Hour} local",
            _options.SweepHourLocal);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun(DateTime.Now, _options.SweepHourLocal);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (stoppingToken.IsCancellationRequested) return;

            try
            {
                using var scope = _services.CreateScope();
                var sweep = scope.ServiceProvider.GetRequiredService<IEcheckSweepService>();
                var result = await sweep.RunAsync("Scheduled", stoppingToken);
                _logger.LogInformation(
                    "eCheck sweep finished: checked={Checked} settled={Settled} returned={Returned} errored={Errored}",
                    result.Checked, result.Settled, result.Returned, result.Errored);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "eCheck sweep run threw; will retry at next scheduled hour");
            }
        }
    }

    // Internal for testability.
    internal static TimeSpan ComputeDelayUntilNextRun(DateTime nowLocal, int targetHour)
    {
        var todayTarget = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, targetHour, 0, 0, nowLocal.Kind);
        var next = nowLocal < todayTarget ? todayTarget : todayTarget.AddDays(1);
        return next - nowLocal;
    }
}
