namespace TSIC.API.Configuration;

/// <summary>
/// Frontend application URL settings for building email links (password reset, etc.)
/// </summary>
public sealed class FrontendSettings
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The PUBLIC host, always — the same value in every environment.
    ///
    /// Used only by <c>EmailTestSendService</c>: a test send rendered on Development carries
    /// <c>http://localhost:4200</c> links, which are dead the moment the mail leaves the box, so
    /// the one thing a test send exists to check — that the link works — is the one thing it
    /// cannot check. The test send rewrites <see cref="BaseUrl"/> to this before transmitting.
    ///
    /// The link then resolves against PRODUCTION, so it proves the URL is well-formed and
    /// reachable, NOT that the sandbox's own data sits behind it. Nothing else reads this;
    /// real sends keep <see cref="BaseUrl"/>.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
