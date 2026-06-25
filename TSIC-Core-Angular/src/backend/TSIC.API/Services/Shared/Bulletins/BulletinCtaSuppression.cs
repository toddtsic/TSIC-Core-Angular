using System.Text.RegularExpressions;
using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// Decides which public-landing bulletins are redundant with a currently-shown
/// quicklink (hero CTA) and can be auto-retired during assembly. A bulletin is
/// redundant ONLY when every link it contains targets a quicklink that the home
/// page is currently displaying AND it carries no editorial substance beyond CTA
/// "glue" text (a deadline, instructions, an image, or a non-CTA link keeps it).
///
/// The active-CTA computation MIRRORS the frontend landing hero
/// (job-landing.component.ts: phase() + CTAS_BY_PHASE + the per-candidate pulse
/// gates) for the ANONYMOUS viewer — keep the two in sync. The href→CTA mapping
/// mirrors TranslateLegacyUrlsPipe (legacy ASP.NET URLs) plus the modern Angular
/// routes a bulletin may already use.
/// </summary>
public static class BulletinCtaSuppression
{
    // Canonical CTA identities shared by both sides of the match.
    private const string Player = "player";
    private const string Team = "team";
    private const string Coach = "coach";
    private const string Referee = "referee";
    private const string Recruiter = "recruiter";
    private const string Rosters = "rosters";
    private const string Schedule = "schedule";
    private const string Store = "store";
    private const string PlayerInsurance = "player-insurance";
    private const string TeamInsurance = "team-insurance";

    /// <summary>HubItem.key → CtaId, matching the frontend hero candidate keys.</summary>
    private static readonly IReadOnlyDictionary<string, string> KeyToCta = new Dictionary<string, string>
    {
        ["register-player"] = Player,
        ["register-team"] = Team,
        ["register-coach"] = Coach,
        ["register-referee"] = Referee,
        ["register-recruiter"] = Recruiter,
        ["view-schedule"] = Schedule,
        ["rosters"] = Rosters,
        ["store"] = Store,
        ["player-insurance"] = PlayerInsurance,
        ["team-insurance"] = TeamInsurance,
    };

    // CTAS_BY_PHASE — the CTA keys eligible in each lifecycle phase (frontend copy).
    private static readonly IReadOnlyDictionary<string, HashSet<string>> CtasByPhase =
        new Dictionary<string, HashSet<string>>
        {
            ["superseded"] = new(),
            ["preview"] = new(),
            ["planned"] = new() { "store" },
            ["concluded"] = new() { "pay-balance", "my-teams", "view-schedule", "store", "rosters" },
            ["inSeason"] = new() { "register-player", "register-coach", "register-referee", "register-recruiter", "my-registration", "pay-balance", "my-teams", "view-schedule", "store", "rosters", "player-insurance", "team-insurance" },
            ["preEvent"] = new() { "register-player", "register-coach", "register-referee", "register-recruiter", "my-registration", "pay-balance", "my-teams", "view-schedule", "store", "rosters", "player-insurance", "team-insurance" },
            ["registrationOpen"] = new() { "register-player", "my-registration", "pay-balance", "register-team", "my-teams", "register-coach", "register-referee", "register-recruiter", "view-schedule", "store", "rosters", "player-insurance", "team-insurance" },
        };

    /// <summary>
    /// The set of CTA identities the public landing hero currently shows to an
    /// ANONYMOUS viewer, derived from the job pulse exactly as the frontend does.
    /// </summary>
    public static HashSet<string> ActiveAnonymousCtaIds(JobPulseDto p)
    {
        var phase = ComputePhase(p);
        var allowed = CtasByPhase.TryGetValue(phase, out var s) ? s : new HashSet<string>();
        var ids = new HashSet<string>();

        void Consider(string key, string cta, bool gate)
        {
            if (gate && allowed.Contains(key)) ids.Add(cta);
        }

        // Anonymous viewer: no regId, so register-* candidates are eligible (the
        // hero's `!registered` guard is satisfied) and the personalized my-*/
        // pay-balance candidates never build. Each gates on its own pulse flag.
        Consider("register-player", Player, p.PlayerRegistrationOpen);
        Consider("register-team", Team, p.TeamRegistrationOpen);
        Consider("register-coach", Coach, p.StaffRegistrationOpen);
        Consider("register-referee", Referee, p.RefereeRegistrationOpen);
        Consider("register-recruiter", Recruiter, p.RecruiterRegistrationOpen);
        Consider("view-schedule", Schedule, p.SchedulePublished);
        Consider("store", Store, p.StoreHasActiveItems);
        Consider("rosters", Rosters, p.PublicRostersAvailable);
        // Insurance discovery cards show to anonymous (the My* suppressors are null).
        Consider("player-insurance", PlayerInsurance, p.OfferPlayerRegsaverInsurance);
        Consider("team-insurance", TeamInsurance, p.OfferTeamRegsaverInsurance);

        return ids;
    }

    /// <summary>Lifecycle phase, first-match, mirroring the frontend phase() computed.</summary>
    private static string ComputePhase(JobPulseDto p)
    {
        if (p.SupersededByLaterEvent != null) return "superseded";
        var today = DateTime.Now.Date;
        var lastGame = p.LastGameDate?.Date;
        if (p.SchedulePublished && lastGame != null && lastGame < today) return "concluded";
        var firstGame = p.FirstGameDate?.Date;
        if (p.SchedulePublished && firstGame != null && firstGame <= today) return "inSeason";
        if (p.SchedulePublished) return "preEvent";
        if (p.PlayerRegistrationOpen || p.TeamRegistrationOpen || (p.MyClubRepTeamCount ?? 0) > 0) return "registrationOpen";
        if (p.PlayerRegistrationPlanned || p.AdultRegistrationPlanned || p.PlayerRegOpensSoonest != null) return "planned";
        return "preview";
    }

    /// <summary>Result of classifying one bulletin's resolved HTML.</summary>
    public sealed record Classification(HashSet<string> CtaIds, bool HasNonCtaLink, bool HasResidualContent);

    // "Glue" — CTA-descriptive words + stopwords that are NOT editorial substance.
    private static readonly HashSet<string> GlueWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "click", "here", "link", "below", "above",
        "to", "an", "the", "your", "you", "for", "of", "on", "in", "and", "or", "this", "our", "is", "are",
        "begin", "edit", "start", "sign", "up", "signup", "now", "today", "please",
        "register", "registration", "registering", "registered",
        "self", "roster", "rosters", "rostering", "rostered",
        "player", "players", "coach", "coaches", "team", "teams", "volunteer", "volunteers",
        "director", "approval", "required",
        "all", "must", "be", "order", "participate",
        "view", "see", "schedule", "schedules", "store", "insurance", "regsaver",
    };

    /// <summary>Keep the bulletin once it has MORE than this many significant words.
    /// Low on purpose — under-suppression is far safer than hiding editorial info.</summary>
    private const int ResidualWordThreshold = 2;

    private static readonly Regex AnchorHrefRe = new(
        "<a\\s[^>]*href\\s*=\\s*[\"']([^\"']+)[\"'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnchorElRe = new(
        "<a\\s[^>]*>.*?</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagRe = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex MediaRe = new(
        "<(img|table|iframe|video)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EntityRe = new("&[a-z#0-9]+;", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WordSplitRe = new("[^a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Classify a bulletin's (token-resolved) title + body for quicklink redundancy.
    /// </summary>
    public static Classification Classify(string? titleHtml, string? bodyHtml)
    {
        var title = titleHtml ?? string.Empty;
        var body = bodyHtml ?? string.Empty;
        var combined = $"{title} {body}";

        var ctaIds = new HashSet<string>();
        var hasNonCtaLink = false;
        foreach (Match m in AnchorHrefRe.Matches(combined))
        {
            var (ids, isRealLink) = ResolveHref(m.Groups[1].Value);
            if (ids.Count > 0)
            {
                foreach (var id in ids) ctaIds.Add(id);
            }
            else if (isRealLink)
            {
                hasNonCtaLink = true;
            }
        }

        var hasMedia = MediaRe.IsMatch(body);
        var hasResidualText = SignificantWords(combined) > ResidualWordThreshold;

        return new Classification(ctaIds, hasNonCtaLink, hasMedia || hasResidualText);
    }

    /// <summary>True when the bulletin is fully covered by an active quicklink.</summary>
    public static bool IsCoveredByActiveCtas(Classification c, IReadOnlySet<string> activeCtaIds)
    {
        if (c.CtaIds.Count == 0 || c.HasNonCtaLink || c.HasResidualContent) return false;
        foreach (var id in c.CtaIds)
        {
            if (!activeCtaIds.Contains(id)) return false;
        }
        return true;
    }

    /// <summary>Significant (non-glue) word count after stripping anchors + tags + glue.</summary>
    private static int SignificantWords(string html)
    {
        var s = AnchorElRe.Replace(html, " "); // drop whole <a>…</a> (labels are glue)
        s = TagRe.Replace(s, " ");
        s = s.Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
             .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);
        s = EntityRe.Replace(s, " ");
        var count = 0;
        foreach (var w in WordSplitRe.Split(s))
        {
            if (w.Length >= 2 && !GlueWords.Contains(w)) count++;
        }
        return count;
    }

    /// <summary>
    /// Resolve one anchor href to the hero CTA(s) it targets. Empty list +
    /// isRealLink=true means a genuine non-CTA link (keeps the bulletin);
    /// isRealLink=false means non-navigational (#, empty, javascript:).
    /// </summary>
    private static (List<string> ctaIds, bool isRealLink) ResolveHref(string? href)
    {
        var raw = (href ?? string.Empty).Trim();
        if (raw.Length == 0 || raw == "#" || raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            return (new List<string>(), false);
        }
        var lower = raw.ToLowerInvariant();

        // --- Legacy ASP.NET MVC forms (must precede the modern checks) ---
        if (lower.Contains("startaregistration"))
        {
            if (lower.Contains("bclubrep=true")) return (new List<string> { Team }, true);
            var ids = new List<string>();
            if (lower.Contains("bplayer=true")) ids.Add(Player);
            if (lower.Contains("bstaff=true")) ids.Add(Coach);
            if (ids.Count > 0) return (ids, true);
        }
        if (lower.Contains("rosters/rosterspubliclookuptourny") || lower.Contains("rosters/rosterpubliclookup"))
        {
            return (new List<string> { Rosters }, true);
        }
        if (lower.Contains("schedules/index")) return (new List<string> { Schedule }, true);

        // --- Modern Angular routes (job-relative or absolute; jobPath-agnostic) ---
        if (lower.Contains("/registration/player")) return (new List<string> { Player }, true);
        if (lower.Contains("/registration/team")) return (new List<string> { Team }, true);
        if (lower.Contains("/registration/adult"))
        {
            if (lower.Contains("role=referee")) return (new List<string> { Referee }, true);
            if (lower.Contains("role=recruiter")) return (new List<string> { Recruiter }, true);
            return (new List<string> { Coach }, true); // coach + player-site "unassigned" bucket
        }
        if (lower.Contains("/rosters/public")) return (new List<string> { Rosters }, true);
        if (lower.Contains("/schedule")) return (new List<string> { Schedule }, true);
        if (lower.Contains("/store")) return (new List<string> { Store }, true);
        if (lower.Contains("/playerviupdate")) return (new List<string> { PlayerInsurance }, true);
        if (lower.Contains("/clubrepviupdate")) return (new List<string> { TeamInsurance }, true);

        // A real navigational link that isn't a hero CTA → keep the bulletin.
        return (new List<string>(), true);
    }
}
