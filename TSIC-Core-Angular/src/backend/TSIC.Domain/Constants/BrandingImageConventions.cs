using System.Collections.ObjectModel;

namespace TSIC.Domain.Constants;

/// <summary>
/// Convention names and processing specs for job branding images.
/// Names preserve the legacy file naming pattern (including the single-L "paralax" typo).
/// </summary>
public static class BrandingImageConventions
{
    public const string BannerBackground = "paralaxbackgroundimage";
    public const string BannerOverlay = "paralaxslide1image";
    public const string LogoHeader = "logoheader";

    /// <summary>
    /// Image processing specifications keyed by convention name.
    /// MaxWidth = resize ceiling (aspect ratio preserved), Quality = JPEG/WebP encode quality.
    /// </summary>
    public static readonly ReadOnlyDictionary<string, (int MaxWidth, int Quality)> ImageSpecs =
        new(new Dictionary<string, (int MaxWidth, int Quality)>
        {
            [BannerBackground] = (1920, 85),
            [BannerOverlay] = (800, 90),
            [LogoHeader] = (400, 90),
        });
}
