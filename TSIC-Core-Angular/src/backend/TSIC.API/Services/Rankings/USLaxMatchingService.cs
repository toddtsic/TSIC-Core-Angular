using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TSIC.Contracts.Dtos.Rankings;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Rankings;

/// <summary>
/// Fuzzy matching engine that aligns usclublax.com scraped rankings with registered TSIC teams.
///
/// Algorithm:
///   1. Extract club name, graduation year, and team color from each scraped ranking (ONCE).
///   2. For each registered team candidate, apply binary gates:
///      - Year gate: if both sides have a year, they must match exactly.
///      - Color gate: if one side has a color, the other must have the SAME color.
///   3. Score surviving candidates via weighted Levenshtein similarity:
///      clubSimilarity × clubWeight + teamNameSimilarity × teamWeight + wordMatchBonus (up to 20%).
///   4. Best candidate above 50% threshold wins.
///
/// Pure business logic — no database access, no HTTP calls.
/// </summary>
public sealed class USLaxMatchingService : IUSLaxMatchingService
{
    private readonly ILogger<USLaxMatchingService> _logger;

    // ── Compiled regex (allocated once, thread-safe) ────────────────────────
    private static readonly Regex YearInParens = new(@"\((\d{4})\)", RegexOptions.Compiled);
    private static readonly Regex YearAtEnd = new(@"\b(\d{4})$", RegexOptions.Compiled);
    private static readonly Regex ColorYearPattern = new(
        @"\s+(?:Red|Blue|White|Black|Green|Orange|Purple|Yellow|Pink|Gray|Grey|Navy|Royal|Maroon|Crimson|Scarlet|Gold|Silver|Teal|Lime|Aqua|Violet|Indigo|Magenta|Cyan|Bronze|Copper|Kelly|Forest|Hunter|Electric|Neon|Hot|Dark|Light|Bright|Midnight|Sky|Steel|Powder)\s*\(\d{4}\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ParensYear = new(@"\s*\(\d{4}\)\s*", RegexOptions.Compiled);
    private static readonly Regex MultiSpaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex AgegroupYear = new(@"\b(\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex ColorBeforeYear = new(@"\b(\w+)\s*\((\d{4})\)", RegexOptions.Compiled);

    // ── Static color set (O(1) lookups) ─────────────────────────────────────
    private static readonly HashSet<string> ValidColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Red", "Blue", "White", "Black", "Green", "Orange", "Purple", "Yellow",
        "Pink", "Gray", "Grey", "Navy", "Royal", "Maroon", "Crimson", "Scarlet",
        "Gold", "Silver", "Teal", "Lime", "Aqua", "Violet", "Indigo", "Magenta",
        "Cyan", "Bronze", "Copper", "Kelly", "Forest", "Hunter", "Electric",
        "Neon", "Hot", "Dark", "Light", "Bright", "Midnight", "Sky", "Steel", "Powder"
    };

    // ── Noise words to strip from word-match scoring ────────────────────────
    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lacrosse", "LAX", "Club", "LC"
    };

    private const double MinimumThreshold = 0.50;
    private const double WordMatchBonusCap = 0.20;

    public USLaxMatchingService(ILogger<USLaxMatchingService> logger)
    {
        _logger = logger;
    }

    public AlignmentResultDto AlignRankingsWithTeams(
        List<RankingEntryDto> rankings,
        List<RankingsTeamDto> registeredTeams,
        int clubWeight = 75,
        int teamWeight = 25)
    {
        var clubW = clubWeight / 100.0;
        var teamW = teamWeight / 100.0;

        // Compute HS year mappings ONCE per alignment run
        var hsYearMap = BuildHighSchoolYearMappings();

        var usedTeamIds = new HashSet<Guid>(registeredTeams.Count);
        var usedRankings = new HashSet<int>(rankings.Count); // track by Rank value
        var aligned = new List<AlignedTeamDto>(Math.Min(rankings.Count, registeredTeams.Count));

        foreach (var ranking in rankings)
        {
            if (usedRankings.Contains(ranking.Rank)) continue;

            // Extract attributes ONCE per ranking
            var scrapedClub = ExtractClubName(ranking.Team);
            var scrapedYear = ExtractYear(ranking.Team);
            var scrapedColor = ExtractColor(ranking.Team, excludeClub: scrapedClub);

            var best = FindBestMatch(
                ranking, registeredTeams, usedTeamIds,
                scrapedClub, scrapedYear, scrapedColor,
                clubW, teamW, hsYearMap);

            if (best is null) continue;

            aligned.Add(new AlignedTeamDto
            {
                Ranking = ranking,
                RegisteredTeam = best.Team,
                MatchScore = best.Score,
                MatchReason = best.Reason
            });

            usedTeamIds.Add(best.Team.TeamId);
            usedRankings.Add(ranking.Rank);
        }

        var unmatchedRankings = rankings.Where(r => !usedRankings.Contains(r.Rank)).ToList();
        var unmatchedTeams = registeredTeams.Where(t => !usedTeamIds.Contains(t.TeamId)).ToList();

        _logger.LogInformation(
            "Alignment complete: {Matched}/{Total} teams matched ({Pct:F1}%)",
            aligned.Count, registeredTeams.Count,
            registeredTeams.Count > 0 ? aligned.Count * 100.0 / registeredTeams.Count : 0);

        return new AlignmentResultDto
        {
            Success = true,
            AgeGroup = rankings.FirstOrDefault()?.Team ?? string.Empty,
            LastUpdated = DateTime.UtcNow,
            AlignedTeams = aligned,
            UnmatchedRankings = unmatchedRankings,
            UnmatchedTeams = unmatchedTeams,
            TotalMatches = aligned.Count,
            TotalTeamsInAgeGroup = registeredTeams.Count,
            MatchPercentage = registeredTeams.Count > 0
                ? aligned.Count * 100.0 / registeredTeams.Count : 0
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Core matching
    // ═══════════════════════════════════════════════════════════════════════

    private sealed record MatchCandidate(RankingsTeamDto Team, double Score, string Reason);

    private MatchCandidate? FindBestMatch(
        RankingEntryDto ranking,
        List<RankingsTeamDto> candidates,
        HashSet<Guid> usedTeamIds,
        string scrapedClub, string scrapedYear, string scrapedColor,
        double clubW, double teamW,
        Dictionary<string, int> hsYearMap)
    {
        MatchCandidate? best = null;

        foreach (var team in candidates)
        {
            if (usedTeamIds.Contains(team.TeamId)) continue;

            // ── Binary gate: graduation year ──────────────────────────
            if (!string.IsNullOrEmpty(scrapedYear) && !YearMatches(team, scrapedYear, hsYearMap))
            {
                if (HasIdentifiableYear(team, hsYearMap))
                    continue; // Both have years but they differ → reject
            }

            // ── Binary gate: color ────────────────────────────────────
            var registeredColor = ExtractColor(team.TeamName, excludeClub: null, explicitColor: team.Color);
            var scrapedHasColor = !string.IsNullOrEmpty(scrapedColor);
            var registeredHasColor = !string.IsNullOrEmpty(registeredColor);

            if (scrapedHasColor != registeredHasColor)
                continue; // One has color, other doesn't → reject

            if (scrapedHasColor && !string.Equals(scrapedColor, registeredColor, StringComparison.OrdinalIgnoreCase))
                continue; // Both have colors but different → reject

            // ── Club name similarity (Levenshtein) ────────────────────
            var registeredClub = ExtractRegisteredClubName(team);
            var cleanScraped = CleanClubName(scrapedClub);
            var cleanRegistered = CleanClubName(registeredClub);
            var clubSim = FuzzySimilarity(cleanScraped, cleanRegistered);

            // ── Weighted score ────────────────────────────────────────
            var score = clubSim * clubW;

            // Team designation similarity
            var registeredDesignation = team.TeamName.Contains(':')
                ? team.TeamName[(team.TeamName.IndexOf(':') + 1)..].Trim()
                : team.TeamName;
            var teamSim = TeamNameSimilarity(ranking.Team, registeredDesignation);
            if (teamSim >= 0.5)
                score += teamSim * teamW;

            // Word match bonus (up to 20%)
            var wordBonus = CalculateWordMatchBonus(ranking.Team, registeredDesignation);
            score += wordBonus;

            // Cap at 1.0
            score = Math.Min(score, 1.0);

            if (score < MinimumThreshold || (best is not null && score <= best.Score))
                continue;

            var reason = FormatMatchReason(score, clubSim, scrapedClub, registeredClub,
                scrapedColor, scrapedYear, teamSim, wordBonus, clubW, teamW);

            best = new MatchCandidate(team, score, reason);
        }

        return best;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Extraction helpers (called once per team, not per comparison)
    // ═══════════════════════════════════════════════════════════════════════

    private static string ExtractClubName(string teamName)
    {
        if (string.IsNullOrEmpty(teamName)) return string.Empty;

        var cleaned = ColorYearPattern.Replace(teamName, "").Trim();
        cleaned = ParensYear.Replace(cleaned, " ").Trim();

        var clubWords = new[] { "Lacrosse", "Elite", "Express", "Academy", "Club", "LC", "LAX" };
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < words.Length; i++)
        {
            if (clubWords.Any(cw => words[i].Contains(cw, StringComparison.OrdinalIgnoreCase)))
                return string.Join(" ", words.Take(i + 1));
        }

        return string.Join(" ", words);
    }

    private static string ExtractRegisteredClubName(RankingsTeamDto team)
    {
        // TSIC format: "ClubName: TeamDesignation" — extract before colon
        if (team.TeamName.Contains(':'))
            return team.TeamName[..team.TeamName.IndexOf(':')].Trim();

        return team.ClubName ?? team.TeamName;
    }

    private static string ExtractYear(string teamName)
    {
        if (string.IsNullOrEmpty(teamName)) return string.Empty;

        var m = YearInParens.Match(teamName);
        if (m.Success) return m.Groups[1].Value;

        m = YearAtEnd.Match(teamName);
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Extract team color, excluding any color that appears in the club-name portion.
    /// For registered teams, also checks the explicit Color field.
    /// </summary>
    private static string ExtractColor(string teamName, string? excludeClub, string? explicitColor = null)
    {
        // Check explicit color field first (registered teams only)
        if (!string.IsNullOrWhiteSpace(explicitColor) && ValidColors.Contains(explicitColor.Trim()))
            return explicitColor.Trim();

        if (string.IsNullOrEmpty(teamName)) return string.Empty;

        // Narrow search to designation portion (after colon or after club name)
        var searchArea = teamName;
        if (teamName.Contains(':'))
        {
            searchArea = teamName[(teamName.IndexOf(':') + 1)..].Trim();
        }
        else if (!string.IsNullOrEmpty(excludeClub) &&
                 teamName.StartsWith(excludeClub, StringComparison.OrdinalIgnoreCase))
        {
            searchArea = teamName[excludeClub.Length..].Trim();
        }

        // Method 1: Color before year in parens — "Red (2026)"
        var m = ColorBeforeYear.Match(searchArea);
        if (m.Success && ValidColors.Contains(m.Groups[1].Value))
            return m.Groups[1].Value;

        // Method 2: Any valid color as a separate word
        var words = searchArea.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var clean = word.Trim('(', ')', ',', '.', ':', ';');
            if (ValidColors.Contains(clean))
                return clean;
        }

        // Method 3: Word-boundary check (longer colors first to avoid substring matches)
        foreach (var color in ValidColors.OrderByDescending(c => c.Length))
        {
            if (searchArea.Contains(color, StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(searchArea, $@"\b{Regex.Escape(color)}\b", RegexOptions.IgnoreCase))
            {
                return color;
            }
        }

        return string.Empty;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Year matching
    // ═══════════════════════════════════════════════════════════════════════

    private static bool YearMatches(RankingsTeamDto team, string scrapedYear, Dictionary<string, int> hsYearMap)
    {
        if (!int.TryParse(scrapedYear, out var scrapedGradYear))
            return false;

        // 1. Direct year in team name
        if (!string.IsNullOrEmpty(team.TeamName) &&
            team.TeamName.Contains(scrapedYear, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. HS level → graduation year
        var hsYear = ExtractHsLevel(team.TeamName, hsYearMap);
        if (hsYear.HasValue)
            return hsYear.Value == scrapedGradYear;

        // 3. Age group name contains year
        if (!string.IsNullOrEmpty(team.AgegroupName))
        {
            if (team.AgegroupName.Contains(scrapedYear, StringComparison.OrdinalIgnoreCase))
                return true;

            var m = AgegroupYear.Match(team.AgegroupName);
            if (m.Success && m.Groups[1].Value == scrapedYear)
                return true;
        }

        // 4. Graduation year range
        if (team.GradYearMin.HasValue && team.GradYearMax.HasValue)
            return scrapedGradYear >= team.GradYearMin.Value && scrapedGradYear <= team.GradYearMax.Value;

        // 5. Single grad year
        return team.GradYearMin == scrapedGradYear || team.GradYearMax == scrapedGradYear;
    }

    private static bool HasIdentifiableYear(RankingsTeamDto team, Dictionary<string, int> hsYearMap)
    {
        if (!string.IsNullOrEmpty(team.TeamName))
        {
            if (YearInParens.IsMatch(team.TeamName) || YearAtEnd.IsMatch(team.TeamName))
                return true;
            if (ExtractHsLevel(team.TeamName, hsYearMap).HasValue)
                return true;
        }

        if (!string.IsNullOrEmpty(team.AgegroupName) && AgegroupYear.IsMatch(team.AgegroupName))
            return true;

        return team.GradYearMin.HasValue || team.GradYearMax.HasValue;
    }

    private static int? ExtractHsLevel(string? teamName, Dictionary<string, int> hsYearMap)
    {
        if (string.IsNullOrEmpty(teamName)) return null;

        // Check longer keys first to avoid "Jr" matching before "Junior"
        foreach (var (key, gradYear) in hsYearMap.OrderByDescending(kv => kv.Key.Length))
        {
            if (Regex.IsMatch(teamName, $@"\b{Regex.Escape(key)}\b", RegexOptions.IgnoreCase))
                return gradYear;
        }

        return null;
    }

    /// <summary>
    /// Dynamically maps HS levels to graduation years based on current academic calendar.
    /// Called ONCE per alignment run (not per candidate).
    /// </summary>
    private static Dictionary<string, int> BuildHighSchoolYearMappings()
    {
        var now = DateTime.Now;
        // Academic year: Aug–Jul. Jan–Jul = spring semester of previous fall's academic year.
        var academicYear = now.Month >= 8 ? now.Year : now.Year - 1;
        var seniorGrad = academicYear + 1;

        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Senior"] = seniorGrad, ["Sr"] = seniorGrad,
            ["12"] = seniorGrad, ["12th"] = seniorGrad,
            ["Varsity"] = seniorGrad, ["V"] = seniorGrad,

            ["Junior"] = seniorGrad + 1, ["Jr"] = seniorGrad + 1,
            ["11"] = seniorGrad + 1, ["11th"] = seniorGrad + 1,
            ["JV"] = seniorGrad + 1, ["JV1"] = seniorGrad + 1,

            ["Sophomore"] = seniorGrad + 2, ["Soph"] = seniorGrad + 2,
            ["10"] = seniorGrad + 2, ["10th"] = seniorGrad + 2,
            ["JV2"] = seniorGrad + 2,

            ["Freshman"] = seniorGrad + 3, ["Fresh"] = seniorGrad + 3,
            ["Frosh"] = seniorGrad + 3,
            ["9"] = seniorGrad + 3, ["9th"] = seniorGrad + 3,
            ["F/S"] = seniorGrad + 3, ["FS"] = seniorGrad + 3
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Similarity scoring
    // ═══════════════════════════════════════════════════════════════════════

    private static string CleanClubName(string club)
    {
        if (string.IsNullOrEmpty(club)) return string.Empty;
        return club
            .Replace("Lacrosse", "", StringComparison.OrdinalIgnoreCase)
            .Replace("LAX", "", StringComparison.OrdinalIgnoreCase)
            .Replace("LC", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Club", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();
    }

    private static double FuzzySimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        if (a == b) return 1.0;

        var dist = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return maxLen == 0 ? 1.0 : 1.0 - (double)dist / maxLen;
    }

    private static double TeamNameSimilarity(string scraped, string registered)
    {
        if (string.IsNullOrEmpty(scraped) || string.IsNullOrEmpty(registered)) return 0;

        var a = CleanTeamName(scraped).ToLowerInvariant();
        var b = CleanTeamName(registered).ToLowerInvariant();

        if (a == b) return 1.0;

        var dist = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return maxLen == 0 ? 1.0 : 1.0 - (double)dist / maxLen;
    }

    private static string CleanTeamName(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var cleaned = ParensYear.Replace(name, "")
            .Replace("Lacrosse", "", StringComparison.OrdinalIgnoreCase)
            .Replace("LAX", "", StringComparison.OrdinalIgnoreCase)
            .Replace("LC", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        return MultiSpaces.Replace(cleaned, " ");
    }

    /// <summary>
    /// Bonus for exact significant word matches (up to 20%).
    /// Strips noise words, years, colors, short words before comparing.
    /// </summary>
    private static double CalculateWordMatchBonus(string scrapedFull, string registeredDesignation)
    {
        var scrapedWords = ExtractSignificantWords(scrapedFull);
        var registeredWords = ExtractSignificantWords(registeredDesignation);

        if (scrapedWords.Count == 0) return 0;

        var matches = scrapedWords.Count(sw =>
            registeredWords.Any(rw => string.Equals(rw, sw, StringComparison.OrdinalIgnoreCase)));

        return matches > 0 ? WordMatchBonusCap * matches / scrapedWords.Count : 0;
    }

    private static List<string> ExtractSignificantWords(string text)
    {
        return text
            .Split([' ', '(', ')', ',', '-', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length > 2)
            .Where(w => !int.TryParse(w, out _))
            .Where(w => !ValidColors.Contains(w))
            .Where(w => !NoiseWords.Contains(w))
            .ToList();
    }

    /// <summary>
    /// Classic Levenshtein with single-row optimization (O(min(m,n)) space).
    /// </summary>
    private static int LevenshteinDistance(string source, string target)
    {
        if (source.Length == 0) return target.Length;
        if (target.Length == 0) return source.Length;

        // Ensure source is the shorter string for space optimization
        if (source.Length > target.Length)
            (source, target) = (target, source);

        var prev = new int[source.Length + 1];
        var curr = new int[source.Length + 1];

        for (var i = 0; i <= source.Length; i++)
            prev[i] = i;

        for (var j = 1; j <= target.Length; j++)
        {
            curr[0] = j;
            for (var i = 1; i <= source.Length; i++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                curr[i] = Math.Min(
                    Math.Min(prev[i] + 1, curr[i - 1] + 1),
                    prev[i - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[source.Length];
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Formatting
    // ═══════════════════════════════════════════════════════════════════════

    private static string FormatMatchReason(
        double score, double clubSim, string scrapedClub, string registeredClub,
        string scrapedColor, string scrapedYear, double teamSim, double wordBonus,
        double clubW, double teamW)
    {
        var label = score switch
        {
            >= 0.85 => "Excellent",
            >= 0.65 => "Good",
            >= 0.50 => "Basic",
            _ => "Low"
        };

        var pct = (score * 100).ToString("F0");
        var parts = new List<string>
        {
            $"Club: {clubSim * 100:F0}% ('{scrapedClub}' vs '{registeredClub}', wt: {clubW:P0})"
        };

        parts.Add(!string.IsNullOrEmpty(scrapedColor) ? "Color: MATCH" : "Color: N/A");
        parts.Add(!string.IsNullOrEmpty(scrapedYear) ? "Year: MATCH" : "Year: N/A");
        parts.Add($"Team: {teamSim * 100:F0}% (wt: {teamW:P0})");

        if (wordBonus > 0)
            parts.Add($"Word bonus: +{wordBonus * 100:F0}%");

        return $"{label} match ({pct}%) — {string.Join(", ", parts)}";
    }
}
