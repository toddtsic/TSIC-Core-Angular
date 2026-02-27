namespace TSIC.API.Configuration;

/// <summary>
/// Frontend application URL settings for building email links (password reset, etc.)
/// </summary>
public sealed class FrontendSettings
{
    public string BaseUrl { get; set; } = string.Empty;
}
