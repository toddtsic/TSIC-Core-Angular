namespace TSIC.API.Constants
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:Refactor your code not to use hardcoded absolute paths or URIs.", Justification = "Defaults are config-backed fallbacks; environments should override via env vars.")]
    public static class TsicConstants
    {
        // Prefer configuration via environment variables; fall back to defaults
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:Refactor your code not to use hardcoded absolute paths or URIs.", Justification = "Defaults are config-backed fallbacks; environments should override via env vars.")]
        public static string BaseUrlStatics =>
            System.Environment.GetEnvironmentVariable("TSIC_BASEURL_STATICS") ?? "https://statics.teamsportsinfo.com/";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:Refactor your code not to use hardcoded absolute paths or URIs.", Justification = "Defaults are config-backed fallbacks; environments should override via env vars.")]
        public static string BaseUrlCrystalReports =>
            System.Environment.GetEnvironmentVariable("TSIC_BASEURL_CR") ?? "https://cr2025.teamsportsinfo.com/api/";

        public const string SuperUserId = "71765055-647D-432E-AFB6-0F84218D0247";
    }
}
