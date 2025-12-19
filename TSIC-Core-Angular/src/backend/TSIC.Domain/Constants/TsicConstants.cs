namespace TSIC.Domain.Constants
{
    /// <summary>
    /// Centralized domain constants. Base URLs support environment variable overrides.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:Refactor your code not to use hardcoded absolute paths or URIs.", Justification = "Defaults are config-backed fallbacks; environments should override via env vars.")]
    public static class TsicConstants
    {
        /// <summary>
        /// Base URL for static assets (images, logos, etc.).
        /// Override via TSIC_BASEURL_STATICS environment variable.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:Refactor your code not to use hardcoded absolute paths or URIs.", Justification = "Defaults are config-backed fallbacks; environments should override via env vars.")]
        public static string BaseUrlStatics =>
            System.Environment.GetEnvironmentVariable("TSIC_BASEURL_STATICS") ?? "https://statics.teamsportsinfo.com/";

        /// <summary>
        /// Base URL for Crystal Reports API.
        /// Override via TSIC_BASEURL_CR environment variable.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:Refactor your code not to use hardcoded absolute paths or URIs.", Justification = "Defaults are config-backed fallbacks; environments should override via env vars.")]
        public static string BaseUrlCrystalReports =>
            System.Environment.GetEnvironmentVariable("TSIC_BASEURL_CR") ?? "https://cr2025.teamsportsinfo.com/api/";

        /// <summary>
        /// Super User GUID for system-level operations.
        /// </summary>
        public const string SuperUserId = "71765055-647D-432E-AFB6-0F84218D0247";

        /// <summary>
        /// Support email address for customer communications.
        /// </summary>
        public const string SupportEmail = "support@teamsportsinfo.com";
    }
}
