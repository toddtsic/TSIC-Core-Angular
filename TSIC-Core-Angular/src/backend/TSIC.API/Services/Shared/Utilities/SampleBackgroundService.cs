using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TSIC.API.Services.Shared.Utilities;

internal class SampleBackgroundService(ILogger<SampleBackgroundService> logger) : BackgroundService
{
    private readonly ILogger<SampleBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SampleBackgroundService starting.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("SampleBackgroundService heartbeat at {time}", DateTimeOffset.Now);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown
        }

        _logger.LogInformation("SampleBackgroundService stopping.");
    }
}
