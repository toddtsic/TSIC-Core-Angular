using TSIC.API.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Sandbox-only delivery of an already-rendered email to every active Superuser inbox, using the
/// forced-transmit override (<c>sendInDevelopment: true</c> — same mechanism as the Staging invite
/// test inbox and the email troubleshooter's forced test send). The subject is stamped with the
/// real recipient the tokens were rendered for, so the inbox item explains itself.
/// Hard-refuses in Production: this class must never become a live send path.
/// </summary>
public class SuperuserTestSendService : ISuperuserTestSendService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IEmailService _email;
    private readonly IHostEnvironment _env;
    private readonly ILogger<SuperuserTestSendService> _logger;

    public SuperuserTestSendService(
        IRegistrationRepository registrationRepo,
        IEmailService email,
        IHostEnvironment env,
        ILogger<SuperuserTestSendService> logger)
    {
        _registrationRepo = registrationRepo;
        _email = email;
        _env = env;
        _logger = logger;
    }

    public async Task<SuperuserTestSendResponse> SendRenderedAsync(
        string renderedSubject,
        string renderedHtmlBody,
        string renderedForName,
        CancellationToken ct = default)
    {
        // Belt-and-suspenders: every endpoint exposing this also rejects in Production, but the
        // invariant lives HERE so no future caller can forget it.
        if (_env.IsLiveProduction())
        {
            return new SuperuserTestSendResponse
            {
                Sent = false,
                RenderedFor = renderedForName,
                Recipients = [],
                Message = "Superuser test sends are not permitted in Production."
            };
        }

        var recipients = await _registrationRepo.GetSuperuserEmailsAsync(ct);
        if (recipients.Count == 0)
        {
            return new SuperuserTestSendResponse
            {
                Sent = false,
                RenderedFor = renderedForName,
                Recipients = [],
                Message = "No active Superuser accounts with an email address were found."
            };
        }

        var ok = await _email.SendAsync(new EmailMessageDto
        {
            FromName = "TEAMSPORTSINFO.COM",
            ToAddresses = recipients,
            Subject = $"[TEST — rendered for: {renderedForName}] {renderedSubject}",
            HtmlBody = renderedHtmlBody
        }, sendInDevelopment: true, cancellationToken: ct);

        _logger.LogInformation(
            "Superuser test send ({Outcome}) to {Count} inbox(es), rendered for {RenderedFor}",
            ok ? "sent" : "failed", recipients.Count, renderedForName);

        return new SuperuserTestSendResponse
        {
            Sent = ok,
            RenderedFor = renderedForName,
            Recipients = recipients,
            Message = ok ? null : "SES transmission failed — check API logs."
        };
    }
}
