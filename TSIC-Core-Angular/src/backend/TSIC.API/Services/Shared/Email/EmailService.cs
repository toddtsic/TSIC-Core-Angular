using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Net;
using System.IO;
using System.Linq;
using TSIC.Domain.Constants;
using TSIC.Contracts.Services;

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

    public async Task<bool> SendAsync(EmailMessageDto messageDto, bool sendInDevelopment = false, CancellationToken cancellationToken = default)
    {
        if (messageDto is null)
        {
            _logger.LogWarning("Email send skipped: null message");
            return false;
        }

        if (!_settings.EmailingEnabled)
        {
            // Short-circuit success when emailing disabled.
            _logger.LogInformation("Emailing disabled; treating message to {Recipients} as sent.", string.Join(",", (messageDto.ToAddresses ?? new List<string>())));
            return true;
        }

        if (_env.IsDevelopment() && !sendInDevelopment)
        {
            _logger.LogInformation("Development environment and sendInDevelopment flag false; skipping SES transmission.");
            return true;
        }

        try
        {
            var message = BuildMimeMessage(messageDto);
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

    public async Task<EmailBatchSendResult> SendBatchAsync(IEnumerable<EmailMessageDto> messages, CancellationToken cancellationToken = default)
    {
        var result = new EmailBatchSendResult();
        foreach (var dto in messages)
        {
            var tos = dto?.ToAddresses?.Distinct() ?? Enumerable.Empty<string>();
            foreach (var to in tos)
            {
                if (!result.AllAddresses.Contains(to))
                {
                    result.AllAddresses.Add(to);
                }
            }
            var success = await SendAsync(dto!, sendInDevelopment: false, cancellationToken);
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

    private MimeMessage BuildMimeMessage(EmailMessageDto dto)
    {
        var message = new MimeMessage();
        var fromName = string.IsNullOrWhiteSpace(dto.FromName) ? "TEAMSPORTSINFO.COM" : dto.FromName;
        var fromAddress = string.IsNullOrWhiteSpace(dto.FromAddress) ? TsicConstants.SupportEmail : dto.FromAddress!;
        message.From.Add(new MailboxAddress(fromName!, fromAddress!));

        if (dto.ToAddresses != null)
        {
            foreach (var to in dto.ToAddresses.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                message.To.Add(MailboxAddress.Parse(to));
            }
        }
        if (dto.CcAddresses != null)
        {
            foreach (var cc in dto.CcAddresses.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                message.Cc.Add(MailboxAddress.Parse(cc));
            }
        }
        if (dto.BccAddresses != null)
        {
            foreach (var bcc in dto.BccAddresses.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                message.Bcc.Add(MailboxAddress.Parse(bcc));
            }
        }

        message.Subject = dto.Subject ?? string.Empty;

        var builder = new BodyBuilder
        {
            HtmlBody = dto.HtmlBody,
            TextBody = dto.TextBody
        };
        message.Body = builder.ToMessageBody();
        return message;
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
