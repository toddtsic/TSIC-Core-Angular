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
            _logger.LogInformation(
                "Sandbox environment (non-Phoenix) and sendInDevelopment flag false; skipping SES transmission to {Recipients}. Subject: {Subject}",
                string.Join(",", messageDto.ToAddresses ?? []), messageDto.Subject);
            return true;
        }

        // Both short-circuits above return TRUE — the caller cannot tell "sent" from "deliberately not
        // sent", which is why a missing email has no trace anywhere. Log the attempt itself so the Seq
        // trail always contains the moment SES was actually reached.
        _logger.LogInformation(
            "SES send attempt: to={Recipients} subject={Subject} sandbox={Sandbox} sendInDevelopment={SendInDev}",
            string.Join(",", messageDto.ToAddresses ?? []), messageDto.Subject, _env.IsSandbox(), sendInDevelopment);

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
            if (ok)
            {
                // The SES message id is the only handle that ties our send to a bounce, a complaint, or a
                // support ticket about a mail that never arrived. It was being discarded.
                _logger.LogInformation(
                    // TWO ids, and they answer different questions. messageId is SES's — use it for AWS
                    // event-publishing logs and SES support tickets. mimeMessageId is the RFC 5322
                    // Message-ID header — paste it into Gmail as `rfc822msgid:<id>` and the search
                    // bypasses EVERY filter, label, folder, Spam and Trash. An empty result there is the
                    // only proof Gmail never received the message, which is exactly the question that
                    // cost a day on 2026-05-10 and again on 2026-08-25 when mail vanished between a
                    // clean SES accept and the inbox. Logging only SES's id left that unanswerable.
                    "SES accepted: messageId={MessageId} mimeMessageId={MimeMessageId} to={Recipients} subject={Subject}",
                    response.MessageId, message.MessageId,
                    string.Join(",", message.To.Select(t => t.ToString())), messageDto.Subject);
            }
            else
            {
                _logger.LogWarning("SES send failed: {StatusCode} for recipients {Recipients}", response.HttpStatusCode, string.Join(",", message.To.Select(t => t.ToString())));
            }
            return ok;
        }
        catch (Exception ex)
        {
            // Was "Exception sending email via SES" with no subject, no recipient and no From — which is
            // most of what you need to tell a credential problem from a rejected sender from a throttle.
            _logger.LogError(ex,
                "Exception sending email via SES: to={Recipients} subject={Subject} from={From}",
                string.Join(",", messageDto.ToAddresses ?? []), messageDto.Subject, TsicConstants.SupportEmail);
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
        // Cc/Bcc come from operator-typed job config, so they get the same TryParse guard as Reply-To
        // above rather than the throwing Parse used for To. A copy address is an addition to the
        // message, never its purpose: one malformed entry must be skipped, not allowed to throw and
        // deprive the actual registrant of their confirmation. (It did exactly that — every job whose
        // CC/BCC held more than one address was failing its whole send.)
        if (dto.CcAddresses != null)
        {
            foreach (var cc in dto.CcAddresses.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                if (MailboxAddress.TryParse(cc, out var ccMailbox)) message.Cc.Add(ccMailbox);
                else _logger.LogWarning("Skipping unparseable CC address {Address}", cc);
            }
        }
        if (dto.BccAddresses != null)
        {
            foreach (var bcc in dto.BccAddresses.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                if (MailboxAddress.TryParse(bcc, out var bccMailbox)) message.Bcc.Add(bccMailbox);
                else _logger.LogWarning("Skipping unparseable BCC address {Address}", bcc);
            }
        }

        message.Subject = dto.Subject ?? string.Empty;

        var builder = new BodyBuilder
        {
            HtmlBody = dto.HtmlBody,
            TextBody = dto.TextBody
        };

        if (dto.Attachments != null)
        {
            foreach (var attachment in dto.Attachments.Where(a => a.Content.Length > 0))
            {
                builder.Attachments.Add(
                    attachment.FileName,
                    attachment.Content,
                    ContentType.Parse(attachment.ContentType));
            }
        }

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
