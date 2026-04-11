using System.Text.RegularExpressions;

namespace TSIC.Application.Services.Clubs;

/// <summary>
/// Pure business logic for fuzzy matching club names.
/// Combines Levenshtein distance (catches typos) with token/Jaccard similarity
/// (catches word reordering). Also detects mega-club relationships where the
/// same root organization appears with different location suffixes.
/// </summary>
public static partial class ClubNameMatcher
{
    // ── Abbreviation dictionary (whole-token replacements only) ──────────

    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        // Sport-specific
        ["lax"]   = "lacrosse",
        ["lc"]    = "lacrosse club",
        ["fc"]    = "football club",
        ["sc"]    = "soccer club",
        ["yc"]    = "youth club",
        ["fh"]    = "field hockey",

        // Organization types
        ["assn"]  = "association",
        ["assoc"] = "association",
        ["acad"]  = "academy",
        ["ath"]   = "athletics",
        ["athl"]  = "athletic",
        ["org"]   = "organization",
        ["fdn"]   = "foundation",
        ["rec"]   = "recreation",

        // Age groups
        ["jr"]    = "juniors",
        ["jrs"]   = "juniors",
        ["sr"]    = "seniors",
        ["srs"]   = "seniors",

        // Directional
        ["n"]     = "north",
        ["no"]    = "north",
        ["s"]     = "south",
        ["so"]    = "south",
        ["e"]     = "east",
        ["w"]     = "west",
        ["ne"]    = "northeast",
        ["nw"]    = "northwest",
        ["se"]    = "southeast",
        ["sw"]    = "southwest",

        // Administrative
        ["co"]    = "county",
        ["twp"]   = "township",
        ["ctr"]   = "center",
        ["cntr"]  = "center",
        ["intl"]  = "international",
        ["natl"]  = "national",
    };

    // ── Common misspellings ─────────────────────────────────────────────

    private static readonly Dictionary<string, string> Misspellings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lacrsse"]     = "lacrosse",
        ["lacrose"]     = "lacrosse",
        ["lacross"]     = "lacrosse",
        ["laccrosse"]   = "lacrosse",
        ["athletcs"]    = "athletics",
        ["atheltic"]    = "athletic",
        ["athelitics"]  = "athletics",
        ["assocation"]  = "association",
        ["asociation"]  = "association",
        ["acadamy"]     = "academy",
        ["academey"]    = "academy",
        ["futbol"]      = "football",
        ["soccar"]      = "soccer",
        ["soccor"]      = "soccer",
    };

    // ── Filler words stripped during normalization ───────────────────────

    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Articles & connectors
        "the", "of", "and", "a", "an",
        // Sport names — every club in a sport shares these; matching on them
        // produces false positives (e.g. "Aacme Lax" matching every lacrosse club)
        "lacrosse", "soccer", "football", "hockey", "baseball", "softball",
        "basketball", "volleyball", "rugby", "cricket", "tennis",
        "field", "ice",
        // Common org suffixes — too generic to be distinctive
        "club", "team", "teams", "sports", "athletics", "athletic",
        "youth", "juniors", "seniors", "association", "organization",
        "academy", "recreation", "center", "league",
    };

    // ── US state codes (used for mega-club root extraction) ─────────────

    private static readonly HashSet<string> StateCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA",
        "HI","ID","IL","IN","IA","KS","KY","LA","ME","MD",
        "MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ",
        "NM","NY","NC","ND","OH","OK","OR","PA","RI","SC",
        "SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","DC"
    };

    // ── Regex for trailing location patterns ────────────────────────────

    [GeneratedRegex(@"\s*[-–—]\s*[A-Za-z]{2}\s*$")]
    private static partial Regex TrailingDashStateRegex();

    [GeneratedRegex(@"\s*\([A-Za-z]{2}\)\s*$")]
    private static partial Regex TrailingParenStateRegex();

    [GeneratedRegex(@"\s+[A-Za-z]{2}\s*$")]
    private static partial Regex TrailingSpaceStateRegex();

    // ═══════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Normalizes a club name for matching:
    /// 1. Lowercase
    /// 2. Fix common misspellings (whole-token)
    /// 3. Expand abbreviations (whole-token)
    /// 4. Remove filler words
    /// 5. Strip punctuation, collapse whitespace
    /// </summary>
    public static string NormalizeClubName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // Lowercase and strip punctuation (keep letters, digits, whitespace)
        var cleaned = new string(name.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray());

        // Split into tokens for word-boundary-safe replacements
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Fix misspellings (whole-token only)
        for (int i = 0; i < tokens.Count; i++)
        {
            if (Misspellings.TryGetValue(tokens[i], out var corrected))
                tokens[i] = corrected;
        }

        // Expand abbreviations (whole-token only — avoids mangling "falcon" etc.)
        var expanded = new List<string>();
        foreach (var token in tokens)
        {
            if (Abbreviations.TryGetValue(token, out var expansion))
            {
                // Expansion may be multi-word (e.g. "lc" → "lacrosse club")
                expanded.AddRange(expansion.Split(' '));
            }
            else
            {
                expanded.Add(token);
            }
        }

        // Remove filler words
        var filtered = expanded.Where(t => !FillerWords.Contains(t)).ToList();

        return string.Join(" ", filtered);
    }

    /// <summary>
    /// Levenshtein distance-based similarity (0–100). Higher = more similar.
    /// Good for catching typos and minor spelling variations.
    /// </summary>
    public static int CalculateSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 100;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;

        var distance = LevenshteinDistance(s1, s2);
        var maxLen = Math.Max(s1.Length, s2.Length);
        var similarity = (1.0 - (double)distance / maxLen) * 100;
        return (int)Math.Round(similarity);
    }

    /// <summary>
    /// Token/Jaccard similarity (0–100). Higher = more similar.
    /// Good for catching word reordering ("Baltimore Lacrosse" vs "Lacrosse Baltimore").
    /// </summary>
    public static int CalculateTokenSimilarity(string normalized1, string normalized2)
    {
        if (string.IsNullOrWhiteSpace(normalized1) && string.IsNullOrWhiteSpace(normalized2)) return 100;
        if (string.IsNullOrWhiteSpace(normalized1) || string.IsNullOrWhiteSpace(normalized2)) return 0;

        var tokens1 = new HashSet<string>(normalized1.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var tokens2 = new HashSet<string>(normalized2.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (tokens1.Count == 0 && tokens2.Count == 0) return 100;
        if (tokens1.Count == 0 || tokens2.Count == 0) return 0;

        var intersection = tokens1.Intersect(tokens2).Count();
        var union = tokens1.Union(tokens2).Count();

        return (int)Math.Round((double)intersection / union * 100);
    }

    /// <summary>
    /// Composite score: max of Levenshtein and token similarity.
    /// Either signal can trigger a match — typos caught by Levenshtein,
    /// word reordering caught by token matching.
    /// </summary>
    public static int CalculateCompositeScore(string name1, string name2)
    {
        var n1 = NormalizeClubName(name1);
        var n2 = NormalizeClubName(name2);

        var levenshtein = CalculateSimilarity(n1, n2);
        var token = CalculateTokenSimilarity(n1, n2);

        return Math.Max(levenshtein, token);
    }

    /// <summary>
    /// Determines if two club names are similar enough to be considered potential duplicates.
    /// </summary>
    public static bool AreSimilar(string name1, string name2, int threshold = 75)
    {
        return CalculateCompositeScore(name1, name2) >= threshold;
    }

    /// <summary>
    /// Extracts the root organization name by stripping trailing location patterns.
    /// Detects mega-club relationships:
    ///   "3 Point Lacrosse - VA" → "3 Point Lacrosse"
    ///   "3 Point Lacrosse (NC)" → "3 Point Lacrosse"
    ///   "Crabs MD"              → "Crabs"
    /// Returns null if no location suffix was found (not a location-qualified name).
    /// </summary>
    public static string? ExtractClubRoot(string clubName)
    {
        if (string.IsNullOrWhiteSpace(clubName)) return null;

        var name = clubName.Trim();

        // Pattern 1: "Club Name - VA"
        var match = TrailingDashStateRegex().Match(name);
        if (match.Success)
        {
            var suffix = match.Value.Trim().TrimStart('-', '–', '—').Trim();
            if (StateCodes.Contains(suffix))
                return name[..match.Index].Trim();
        }

        // Pattern 2: "Club Name (VA)"
        match = TrailingParenStateRegex().Match(name);
        if (match.Success)
        {
            var suffix = match.Value.Trim().Trim('(', ')').Trim();
            if (StateCodes.Contains(suffix))
                return name[..match.Index].Trim();
        }

        // Pattern 3: "Club Name VA" (trailing 2-letter state code after space)
        match = TrailingSpaceStateRegex().Match(name);
        if (match.Success)
        {
            var suffix = match.Value.Trim();
            // Only if 2 uppercase letters AND a known state code
            // (avoid stripping real name parts like "FC")
            if (suffix.Length == 2 && StateCodes.Contains(suffix))
                return name[..match.Index].Trim();
        }

        return null; // No location suffix detected
    }

    /// <summary>
    /// Checks if two clubs share the same root organization (mega-club pattern).
    /// Returns true if both have location suffixes and their roots match at 85%+.
    /// </summary>
    public static bool AreRelatedClubs(string name1, string name2)
    {
        var root1 = ExtractClubRoot(name1);
        var root2 = ExtractClubRoot(name2);

        // Both must have a location suffix to be considered related
        if (root1 == null || root2 == null) return false;

        return CalculateCompositeScore(root1, root2) >= 85;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    private static int LevenshteinDistance(string s1, string s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;
        var matrix = new int[len1 + 1, len2 + 1];

        for (int i = 0; i <= len1; i++) matrix[i, 0] = i;
        for (int j = 0; j <= len2; j++) matrix[0, j] = j;

        for (int i = 1; i <= len1; i++)
        {
            for (int j = 1; j <= len2; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost
                );
            }
        }

        return matrix[len1, len2];
    }
}
