namespace TSIC.Domain.Constants
{
    /// <summary>
    /// Centralized domain constants. Base URLs support environment variable overrides.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:Refactor your code not to use hardcoded absolute paths or URIs.", Justification = "Defaults are config-backed fallbacks; environments should override via env vars.")]
    public static class TsicConstants
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:Refactor your code not to use hardcoded absolute paths or URIs.", Justification = "Fallback only; the API sets the real value from appsettings at startup.")]
        private static string _baseUrlStatics = "https://statics.teamsportsinfo.com/";

        /// <summary>
        /// Base URL for static assets (images, logos, etc.), always ending in "/".
        ///
        /// Set ONCE at startup from appsettings TsicSettings:StaticsBaseUrl (Program.cs calls
        /// <see cref="UseStaticsBaseUrl"/>), so appsettings is the one place the statics address
        /// is configured. This used to read the TSIC_BASEURL_STATICS environment variable -- a
        /// second setting for the same address, so an environment could set one, miss the other,
        /// and quietly serve some links from another environment's statics. The fallback above only
        /// serves code that runs without the API host (unit tests).
        /// </summary>
        public static string BaseUrlStatics => _baseUrlStatics;

        /// <summary>
        /// Startup only: sets <see cref="BaseUrlStatics"/> from TsicSettings:StaticsBaseUrl.
        /// </summary>
        public static void UseStaticsBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("TsicSettings:StaticsBaseUrl is empty.", nameof(url));
            _baseUrlStatics = url.TrimEnd('/') + "/";
        }

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

        /// <summary>
        /// How long an emailed password-reset link stays valid.
        ///
        /// ONE source for three places that must never drift apart: the Identity token lifespan
        /// (Program.cs, DataProtectionTokenProviderOptions), the "expires in ..." sentence in the
        /// reset email (AuthController.BuildPasswordResetEmail), and the [STARTUP-CONFIG] audit line.
        /// A link that outlives its own sentence -- or a sentence that outlives the link -- produces
        /// the one failure the user cannot self-diagnose, because an expired token and a broken one
        /// give the identical message.
        ///
        /// Raised from 1h to 8h: parents read email on their own schedule, and every link that dies
        /// before it is clicked is a support call. The token is single-use (a successful reset rotates
        /// the security stamp it embeds) and only ever reaches an address already on the account, so
        /// the extra window costs little. 8h does not cover a full overnight -- a 9pm request clicked
        /// at 7am is still expired.
        /// </summary>
        public const int PasswordResetTokenLifespanHours = 8;

        /// <summary>
        /// Human-readable form of <see cref="PasswordResetTokenLifespanHours"/>, for email copy.
        /// </summary>
        public static string PasswordResetTokenLifespanText =>
            PasswordResetTokenLifespanHours == 1 ? "1 hour" : $"{PasswordResetTokenLifespanHours} hours";
    }
}
