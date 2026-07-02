using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Net;
using System.IO;
using System.Linq;
using TSIC.API.Extensions;
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

        if (_env.IsSandbox() && !sendInDevelopment)
        {
            _logger.LogInformation("Sandbox environment (non-Phoenix) and sendInDevelopment flag false; skipping SES transmission.");
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
        var fromName = string.IsNullOrWhiteSpace(dto.FromName) ? "TEAMSPORTSINFO.COM" : dto.FromName!;
        // SES only accepts the verified sender identity, so the From/Sender ADDRESS is always support@.
        // A caller's FromName is display intent only; the real human (a sending admin, a job's configured
        // contact) rides Reply-To. NormalizeFromHeader re-asserts this address as a final backstop.
        var verifiedFrom = TsicConstants.SupportEmail;
        message.From.Add(new MailboxAddress(fromName, verifiedFrom));
        message.Sender = new MailboxAddress(fromName, verifiedFrom);

        // Reply-To routes replies to the real sender when supplied and parseable; otherwise it falls
        // back to the From identity. TryParse guards free-text config (e.g. a job's RegFormFrom that
        // holds a name rather than an address) from throwing MimeKit's addr-spec parse exception.
        if (!string.IsNullOrWhiteSpace(dto.ReplyToAddress) &&
            MailboxAddress.TryParse(dto.ReplyToAddress, out var replyMailbox))
        {
            if (!string.IsNullOrWhiteSpace(dto.ReplyToName)) replyMailbox.Name = dto.ReplyToName!;
            message.ReplyTo.Add(replyMailbox);
        }
        else
        {
            message.ReplyTo.Add(new MailboxAddress(fromName, verifiedFrom));
        }

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

    // Single write-side chokepoint for the SES verified-identity invariant: EVERY outbound message's
    // From address is forced to support@teamsportsinfo.com here, regardless of what any caller set.
    // This is what makes an unverified/invalid From (a job name, an admin's personal email, free-text
    // config) impossible to transmit — the real human is expected on Reply-To (set in BuildMimeMessage).
    private void NormalizeFromHeader(MimeMessage message)
    {
        var name = message.From.Mailboxes.FirstOrDefault()?.Name;
        var displayName = string.IsNullOrWhiteSpace(name) ? "TEAMSPORTSINFO.COM" : name!;
        var brandedName = displayName.Contains("TEAMSPORTSINFO", StringComparison.OrdinalIgnoreCase)
            ? displayName
            : $"{displayName} (TEAMSPORTSINFO.COM)";
        message.From.Clear();
        message.From.Add(new MailboxAddress(brandedName, TSIC.Domain.Constants.TsicConstants.SupportEmail));
    }
}
