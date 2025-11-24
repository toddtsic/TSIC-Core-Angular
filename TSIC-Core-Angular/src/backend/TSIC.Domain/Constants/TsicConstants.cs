namespace TSIC.Domain.Constants
{
    // Centralized immutable constants. External base URLs intentionally hard-coded
    // for current deployment topology; guarded with pragma to suppress "hardcoded URI" warning (S1075).
    public static class TsicConstants
    {
#pragma warning disable S1075 // Justification: External CDN / API base endpoints are fixed; configuration indirection not yet required.
        public const string BaseUrlStatics = "https://statics.teamsportsinfo.com/";
        public const string BaseUrlCrystalReports = "https://cr2025.teamsportsinfo.com/api/";
#pragma warning restore S1075
        public const string SuperUserId = "71765055-647D-432E-AFB6-0F84218D0247";
        public const string SupportEmail = "support@teamsportsinfo.com";
    }
}
