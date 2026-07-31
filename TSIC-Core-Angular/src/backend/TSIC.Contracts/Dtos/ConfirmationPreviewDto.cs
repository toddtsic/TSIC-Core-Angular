namespace TSIC.Contracts.Dtos;

/// <summary>
/// A confirmation email rendered but NOT sent, for delivery to a test inbox off-Production.
///
/// Exists so the test path can reuse the real render: a preview produced by a second, parallel
/// rendering path is not a preview of anything. The confirmation senders (player / adult / club rep)
/// each compose-and-send internally, so each exposes a render-only seam that returns this.
///
/// Deliberately NOT a recipient override on the live send. The address swap happens downstream in
/// <c>EmailTestSendService</c>, which hard-refuses in Production — keeping the real send path
/// incapable of being redirected at all.
/// </summary>
public record ConfirmationPreviewDto
{
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }

    /// <summary>Whose data the tokens resolved against — stamped into the test subject line.</summary>
    public required string RenderedForName { get; init; }
}
