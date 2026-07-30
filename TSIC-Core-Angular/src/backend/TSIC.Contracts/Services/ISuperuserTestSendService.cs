namespace TSIC.Contracts.Services;

/// <summary>
/// Sandbox-only "see the real email" test send: delivers an already-rendered message to every
/// active Superuser's inbox via the forced-transmit override (the same mechanism as the Staging
/// invite test inbox). Refuses outright in Production — this is a preview tool, never a send path.
/// Shared by every compose surface's test endpoint; callers render with their own pipeline
/// (TextSubstitution, ARB tokens, USLax extras) and hand the finished subject/body here.
/// </summary>
public interface ISuperuserTestSendService
{
    Task<SuperuserTestSendResponse> SendRenderedAsync(
        string renderedSubject,
        string renderedHtmlBody,
        string renderedForName,
        CancellationToken ct = default);
}

public record SuperuserTestSendResponse
{
    public required bool Sent { get; init; }
    /// <summary>Name of the real recipient whose registration context the tokens were rendered with.</summary>
    public required string RenderedFor { get; init; }
    /// <summary>Superuser inboxes the test was delivered to.</summary>
    public required List<string> Recipients { get; init; }
    public string? Message { get; init; }
}
