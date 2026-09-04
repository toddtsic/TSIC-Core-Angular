namespace TSIC.API.Services.Usage;

/// <summary>
/// Turns the two raw strings a request carries -- the ?xc= client tag and the
/// User-Agent -- into the small integer dimensions logs.AppUsage stores. Pure
/// functions, no state, no I/O; runs on the writer thread, never on the request path.
///
/// Every id here matches a seeded row in the logs lookup tables. Id 0 is the explicit
/// "unknown" member in each: this never returns a value the fact table's foreign keys
/// would reject, and never returns NULL where the schema says NOT NULL.
/// </summary>
public static class UsageClassifier
{
    /// <summary>
    /// The reserved query-string parameter every client stamps on every request.
    ///
    /// RESERVED: no endpoint may ever bind a parameter named "xc". It carries client
    /// identity only, and an action that also bound it would silently take a value the
    /// caller never meant for it.
    ///
    /// A constant rather than two string literals because the usage filter and the Seq
    /// enricher both read it -- if they ever disagreed on the key, one of them would
    /// silently report every request as an unknown client.
    ///
    /// If a caching CDN is ever put in front of the API, xc MUST be excluded from the
    /// cache key: it varies per app/version/platform and would otherwise shard the
    /// cache into near-duplicates of the same response.
    /// </summary>
    public const string ClientTagQueryKey = "xc";

    // logs.AppClients
    public const int AppClientUnknown = 0;
    public const int AppClientTeams = 1;
    public const int AppClientEvents = 2;
    public const int AppClientWeb = 3;

    // logs.Platforms
    public const int PlatformUnknown = 0;
    public const int PlatformIos = 1;
    public const int PlatformAndroid = 2;
    public const int PlatformWeb = 3;

    // logs.Browsers
    public const int BrowserUnknown = 0;
    public const int BrowserChrome = 1;
    public const int BrowserSafari = 2;
    public const int BrowserEdge = 3;
    public const int BrowserFirefox = 4;
    public const int BrowserWebView = 5;
    public const int BrowserOther = 6;

    // logs.DeviceClasses
    public const int DeviceUnknown = 0;
    public const int DevicePhone = 1;
    public const int DeviceTablet = 2;
    public const int DeviceDesktop = 3;

    /// <summary>logs.AppUsage.AppVersion is VARCHAR(32).</summary>
    public const int MaxAppVersionLength = 32;

    /// <summary>
    /// Parses <c>?xc=name/version (platform)</c>, e.g. <c>tsic-teams/5.4.1.82 (ios)</c>.
    ///
    /// This tag is the ONLY way to tell the two native apps apart: both are the same
    /// WKWebView and send an identical User-Agent, so User-Agent sniffing cannot do it.
    /// A missing or unparseable tag yields unknown/unknown/"" and the row is still
    /// kept -- installed app versions predating the tag are expected to report that
    /// way until the stores turn over.
    ///
    /// A query parameter and not a custom header (rev 3): a custom header makes an
    /// otherwise-simple GET non-simple, so every anonymous Events request would buy a
    /// CORS preflight round-trip. The same string in the URL carries identical
    /// information at identical cost with no preflight, by construction.
    ///
    /// Takes the value already decoded by <c>Request.Query</c>, so the caller passes
    /// what the client sent, percent-escapes resolved.
    /// </summary>
    public static (int AppClientId, int PlatformId, string AppVersion) ParseClientTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return (AppClientUnknown, PlatformUnknown, string.Empty);

        var span = tag.AsSpan().Trim();

        // platform lives in the trailing "(...)"
        var platformId = PlatformUnknown;
        var open = span.LastIndexOf('(');
        var close = span.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            platformId = span[(open + 1)..close].Trim() switch
            {
                "ios" => PlatformIos,
                "android" => PlatformAndroid,
                "web" => PlatformWeb,
                _ => PlatformUnknown,
            };
            span = span[..open].Trim();
        }

        // name/version
        var name = span;
        var version = ReadOnlySpan<char>.Empty;
        var slash = span.IndexOf('/');
        if (slash >= 0)
        {
            name = span[..slash].Trim();
            version = span[(slash + 1)..].Trim();
        }

        var appClientId = name switch
        {
            "tsic-teams" => AppClientTeams,
            "tsic-events" => AppClientEvents,
            "tsic-web" => AppClientWeb,
            _ => AppClientUnknown,
        };

        return (appClientId, platformId, SanitizeVersion(version));
    }

    /// <summary>
    /// Mirrors the clients' own sanitize() (X-Client rollout rev 3): charset
    /// [0-9A-Za-z.-], illegal characters REPLACED with '-' rather than deleted, then
    /// capped. Done again server-side because the column is NOT NULL with no DEFAULT --
    /// an oversized or exotic value must not be able to fail the insert of a batch.
    ///
    /// The native apps append the build number as version.build ("5.4.1.82"): two
    /// builds of one store version are different binaries, and "which build broke it"
    /// is the first triage question. '+' was the separator under rev 2 and is now NOT
    /// in the charset -- a raw '+' in a query string decodes to a SPACE, so a rev-2
    /// client's "5.4.1+82" arrives here as "5.4.1 82" and lands as "5.4.1-82".
    /// Visibly mangled, which is the correct outcome: no fielded client sends that
    /// form (rev 2 never reached the stores), so there is nothing to accommodate.
    ///
    /// Replacing rather than deleting is the other half of that: deletion turns a
    /// mangled version into one that still looks valid, so corruption reads as fact.
    /// A '-' leaves it visible.
    /// </summary>
    private static string SanitizeVersion(ReadOnlySpan<char> version)
    {
        if (version.IsEmpty) return string.Empty;

        var length = Math.Min(version.Length, MaxAppVersionLength);
        Span<char> buffer = stackalloc char[MaxAppVersionLength];

        for (var i = 0; i < length; i++)
        {
            var c = version[i];
            var ok = (c >= '0' && c <= '9')
                  || (c >= 'A' && c <= 'Z')
                  || (c >= 'a' && c <= 'z')
                  || c == '.' || c == '-';
            buffer[i] = ok ? c : '-';
        }

        return new string(buffer[..length]);
    }

    /// <summary>
    /// Coarse User-Agent classification. Deliberately a short substring ladder rather
    /// than a UA-parsing dependency: the fact table stores four buckets, not a version
    /// matrix, and a library here would be a permanently-stale dependency bought for
    /// resolution nobody reports on.
    /// </summary>
    public static (bool IsBot, int BrowserId, int DeviceClassId) ClassifyUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return (false, BrowserUnknown, DeviceUnknown);

        var ua = userAgent.ToLowerInvariant();

        if (IsBotAgent(ua))
            return (true, BrowserOther, DeviceUnknown);

        // Order matters. Edge and modern Chrome both claim "chrome"; every iOS browser
        // claims "safari"; embedded webviews claim whatever engine they host. Most
        // specific marker first, or everything collapses into chrome/safari.
        var browserId =
            ua.Contains("edg/") || ua.Contains("edge") ? BrowserEdge
            : ua.Contains("firefox") || ua.Contains("fxios") ? BrowserFirefox
            : IsWebView(ua) ? BrowserWebView
            : ua.Contains("chrome") || ua.Contains("crios") ? BrowserChrome
            : ua.Contains("safari") ? BrowserSafari
            : BrowserOther;

        var deviceClassId =
            ua.Contains("ipad") || (ua.Contains("android") && !ua.Contains("mobile")) ? DeviceTablet
            : ua.Contains("mobile") || ua.Contains("iphone") || ua.Contains("ipod") || ua.Contains("android") ? DevicePhone
            : DeviceDesktop;

        return (false, browserId, deviceClassId);
    }

    private static bool IsBotAgent(string ua) =>
        ua.Contains("bot") || ua.Contains("crawler") || ua.Contains("spider")
        || ua.Contains("slurp") || ua.Contains("headless") || ua.Contains("phantomjs")
        || ua.Contains("curl/") || ua.Contains("wget/") || ua.Contains("python-requests")
        || ua.Contains("postman") || ua.Contains("monitoring") || ua.Contains("uptime");

    // wv = Android WebView. "; wv)" is the reliable marker. iOS WKWebView omits the
    // Safari token that mobile Safari always sends, which is what the second test finds.
    private static bool IsWebView(string ua) =>
        ua.Contains("; wv)")
        || (ua.Contains("applewebkit") && !ua.Contains("safari") && !ua.Contains("chrome"));

    /// <summary>
    /// Id -> name, for Seq only. The fact table stores the id; a log line storing "1"
    /// would be unreadable and would silently rot the first time the seed changes.
    /// </summary>
    public static string AppClientName(int appClientId) => appClientId switch
    {
        AppClientTeams => "tsic-teams",
        AppClientEvents => "tsic-events",
        AppClientWeb => "tsic-web",
        _ => "unknown",
    };

    /// <inheritdoc cref="AppClientName"/>
    public static string PlatformName(int platformId) => platformId switch
    {
        PlatformIos => "ios",
        PlatformAndroid => "android",
        PlatformWeb => "web",
        _ => "unknown",
    };
}
