using TSIC.API.Extensions;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Sandbox-only delivery of an already-rendered email to a single test inbox, using the
/// forced-transmit override (<c>sendInDevelopment: true</c> — same mechanism as the Staging invite
/// test inbox and the email troubleshooter's forced test send). The subject is stamped with the
/// real recipient the tokens were rendered for, so the inbox item explains itself.
/// Hard-refuses in Production: this class must never become a live send path.
/// </summary>
public class EmailTestSendService : IEmailTestSendService
{
    private readonly IEmailService _email;
    private readonly IHostEnvironment _env;
    private readonly ILogger<EmailTestSendService> _logger;

    public EmailTestSendService(
        IEmailService email,
        IHostEnvironment env,
        ILogger<EmailTestSendService> logger)
    {
        _email = email;
        _env = env;
        _logger = logger;
    }

    public async Task<EmailTestSendResponse> SendRenderedAsync(
        string renderedSubject,
        string renderedHtmlBody,
        string renderedForName,
        string testRecipient,
        CancellationToken ct = default)
    {
        // Belt-and-suspenders: every endpoint exposing this also rejects in Production, but the
        // invariant lives HERE so no future caller can forget it.
        if (_env.IsLiveProduction())
        {
            return new EmailTestSendResponse
            {
                Sent = false,
                RenderedFor = renderedForName,
                Recipient = string.Empty,
                Message = "Test sends are not permitted in Production."
            };
        }

        var recipient = testRecipient.Trim();
        if (!recipient.Contains('@'))
        {
            return new EmailTestSendResponse
            {
                Sent = false,
                RenderedFor = renderedForName,
                Recipient = recipient,
                Message = "A valid test recipient email is required."
            };
        }

        var ok = await _email.SendAsync(new EmailMessageDto
        {
            FromName = "TEAMSPORTSINFO.COM",
            ToAddresses = new List<string> { recipient },
            Subject = $"[TEST — rendered for: {renderedForName}] {renderedSubject}",
            HtmlBody = renderedHtmlBody
        }, sendInDevelopment: true, cancellationToken: ct);

        _logger.LogInformation(
            "Email test send ({Outcome}) to {Recipient}, rendered for {RenderedFor}",
            ok ? "sent" : "failed", recipient, renderedForName);

        return new EmailTestSendResponse
        {
            Sent = ok,
            RenderedFor = renderedForName,
            Recipient = recipient,
            Message = ok ? null : "SES transmission failed — check API logs."
        };
    }
}
