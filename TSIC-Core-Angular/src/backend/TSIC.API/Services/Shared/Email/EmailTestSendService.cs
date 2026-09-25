using Microsoft.Extensions.Options;
using TSIC.API.Configuration;
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
    private readonly string _baseUrl;
    private readonly string _publicBaseUrl;

    public EmailTestSendService(
        IEmailService email,
        IHostEnvironment env,
        IOptions<FrontendSettings> frontendSettings,
        ILogger<EmailTestSendService> logger)
    {
        _email = email;
        _env = env;
        _logger = logger;
        _baseUrl = (frontendSettings.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        _publicBaseUrl = (frontendSettings.Value.PublicBaseUrl ?? string.Empty).TrimEnd('/');
    }

    /// <summary>
    /// Point the rendered links at the PUBLIC host.
    ///
    /// Rendering happens against the running environment, so on Development every link in the
    /// letter is <c>http://localhost:4200/...</c> — dead the moment the mail leaves the box, which
    /// defeats the point of a test send. Swapping the host makes the link clickable from a real
    /// inbox. It resolves against PRODUCTION, so it proves the URL is well-formed and reachable,
    /// not that the sandbox's own data sits behind it.
    ///
    /// No-op when the two are already equal, when either is unset, or in Production (which cannot
    /// reach this class at all). Real sends never pass through here.
    /// </summary>
    private string ToPublicHost(string rendered)
    {
        if (string.IsNullOrEmpty(_baseUrl)
            || string.IsNullOrEmpty(_publicBaseUrl)
            || string.Equals(_baseUrl, _publicBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return rendered;
        }
        return rendered.Replace(_baseUrl, _publicBaseUrl, StringComparison.OrdinalIgnoreCase);
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

        var publicBody = ToPublicHost(renderedHtmlBody);

        var ok = await _email.SendAsync(new EmailMessageDto
        {
            FromName = "TEAMSPORTSINFO.COM",
            ToAddresses = new List<string> { recipient },
            Subject = $"[TEST — rendered for: {renderedForName}] {renderedSubject}",
            HtmlBody = publicBody
        }, sendInDevelopment: true, cancellationToken: ct);

        _logger.LogInformation(
            "Email test send ({Outcome}) to {Recipient}, rendered for {RenderedFor}; links point at {LinkHost}",
            ok ? "sent" : "failed", recipient, renderedForName,
            string.IsNullOrEmpty(_publicBaseUrl) ? _baseUrl : _publicBaseUrl);

        return new EmailTestSendResponse
        {
            Sent = ok,
            RenderedFor = renderedForName,
            Recipient = recipient,
            Message = ok ? null : "SES transmission failed — check API logs."
        };
    }
}
