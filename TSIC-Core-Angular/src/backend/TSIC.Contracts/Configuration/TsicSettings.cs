namespace TSIC.Contracts.Configuration;

/// <summary>
/// Global TSIC platform settings (bound from appsettings "TsicSettings" section).
/// </summary>
public class TsicSettings
{
    public const string SectionName = "TsicSettings";

    /// <summary>
    /// The default customer whose ADN credentials are copied when creating a new customer.
    /// </summary>
    public Guid DefaultCustomerId { get; set; }

    /// <summary>
    /// Base URL for the statics CDN (banner images, headshots, etc.).
    /// Example: https://statics.teamsportsinfo.com
    /// </summary>
    public string StaticsBaseUrl { get; set; } = "https://statics.teamsportsinfo.com";
}
