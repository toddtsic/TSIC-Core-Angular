using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Net;
using System.IO;
using System.Linq;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Amazon SES implementation only. Other legacy SMTP providers intentionally removed.
/// </summary>
public sealed class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly IAmazonSimpleEmailService _ses;
    private readonly ILogger<EmailService> _logger;
    private readonly IHostEnvironment _env;

    public EmailService(
        IOptions<EmailSettings> options,
        IAmazonSimpleEmailService ses,
        ILogger<EmailService> logger,
        IHostEnvironment env)
    {
        _settings = options.Value;
        _ses = ses;
        _logger = logger;
        _env = env;
    }

    public async Task<bool> SendAsync(MimeMessage message, bool sendInDevelopment = false, CancellationToken cancellationToken = default)
    {
        if (message is null)
        {
            _logger.LogWarning("Email send skipped: null message");
            return false;
        }

        if (!_settings.EmailingEnabled)
        {
            // Short-circuit success when emailing disabled.
            _logger.LogInformation("Emailing disabled; treating message to {Recipients} as sent.", string.Join(",", message.To.Select(t => t.ToString())));
            return true;
        }

        if (_env.IsDevelopment() && !sendInDevelopment)
        {
            _logger.LogInformation("Development environment and sendInDevelopment flag false; skipping SES transmission.");
            return true;
        }

        try
        {
            NormalizeFromHeader(message);
            using var memory = new MemoryStream();
            await message.WriteToAsync(memory, cancellationToken);
            memory.Position = 0;

            var request = new SendRawEmailRequest
            {
                RawMessage = new RawMessage(memory)
            };
            var response = await _ses.SendRawEmailAsync(request, cancellationToken);
            var ok = response.HttpStatusCode == HttpStatusCode.OK;
            if (!ok)
            {
                _logger.LogWarning("SES send failed: {StatusCode} for recipients {Recipients}", response.HttpStatusCode, string.Join(",", message.To.Select(t => t.ToString())));
            }
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending email via SES");
            return false;
        }
    }

    public async Task<EmailBatchSendResult> SendBatchAsync(IEnumerable<MimeMessage> messages, CancellationToken cancellationToken = default)
    {
        var result = new EmailBatchSendResult();
        foreach (var msg in messages)
        {
            var tos = msg?.To.Mailboxes.Select(m => m.Address).Distinct() ?? Enumerable.Empty<string>();
            foreach (var to in tos)
            {
                if (!result.AllAddresses.Contains(to))
                {
                    result.AllAddresses.Add(to);
                }
            }
            var success = await SendAsync(msg!, sendInDevelopment: false, cancellationToken);
            if (!success)
            {
                foreach (var to in tos)
                {
                    if (!result.FailedAddresses.Contains(to))
                    {
                        result.FailedAddresses.Add(to);
                    }
                }
            }
        }
        return result;
    }

    private void NormalizeFromHeader(MimeMessage message)
    {
        if (message.From.Count == 0)
        {
            message.From.Add(new MailboxAddress("TEAMSPORTSINFO.COM", TSIC.Domain.Constants.TsicConstants.SupportEmail));
            return;
        }
        var originalMailbox = message.From.Mailboxes.FirstOrDefault();
        if (originalMailbox is null) return;
        var name = string.IsNullOrWhiteSpace(originalMailbox.Name) ? "TEAMSPORTSINFO.COM" : originalMailbox.Name;
        var brandedName = name.Contains("TEAMSPORTSINFO", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} (TEAMSPORTSINFO.COM)";
        var mailbox = new MailboxAddress(brandedName, originalMailbox.Address);
        message.From.Clear();
        message.From.Add(mailbox);
    }
}
