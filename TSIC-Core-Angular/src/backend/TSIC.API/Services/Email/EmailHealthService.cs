using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.Extensions.Options;
using TSIC.API.Services.Shared.Email;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Email;

public interface IEmailHealthService
{
    Task<EmailHealthStatus> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class EmailHealthStatus
{
    public bool EmailingEnabled { get; set; }
    public bool IsDevelopment { get; set; }
    public bool SandboxMode { get; set; }
    public bool SesReachable { get; set; }
    public int? Max24HourSend { get; set; }
    public int? SentLast24Hours { get; set; }
    public double? MaxSendRate { get; set; }
    public string Region { get; set; } = string.Empty;
    public string? Warning { get; set; }
}

public sealed class EmailHealthService : IEmailHealthService
{
    private readonly IAmazonSimpleEmailService _ses;
    private readonly EmailSettings _settings;
    private readonly IHostEnvironment _env;
    private readonly ILogger<EmailHealthService> _logger;

    public EmailHealthService(IAmazonSimpleEmailService ses, IOptions<EmailSettings> options, IHostEnvironment env, ILogger<EmailHealthService> logger)
    {
        _ses = ses;
        _settings = options.Value;
        _env = env;
        _logger = logger;
    }

    public async Task<EmailHealthStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var status = new EmailHealthStatus
        {
            EmailingEnabled = _settings.EmailingEnabled,
            IsDevelopment = _env.IsDevelopment(),
            SandboxMode = _settings.SandboxMode,
            Region = _settings.AwsRegion ?? "(default)"
        };

        if (!_settings.EmailingEnabled)
        {
            status.Warning = "Emailing disabled via configuration.";
            return status;
        }

        try
        {
            var quota = await _ses.GetSendQuotaAsync(cancellationToken);
            status.SesReachable = true;
            status.Max24HourSend = (int?)quota.Max24HourSend;
            status.SentLast24Hours = (int?)quota.SentLast24Hours;
            status.MaxSendRate = quota.MaxSendRate;

            if (!status.SandboxMode && status.Max24HourSend.HasValue && status.Max24HourSend < 1000)
            {
                status.Warning = "Low SES 24h send quota; account may still be in sandbox or recently out of trial.";
            }
        }
        catch (Exception ex)
        {
            status.SesReachable = false;
            status.Warning = "Failed to reach SES API.";
            _logger.LogWarning(ex, "SES health check failed.");
        }
        return status;
    }
}
