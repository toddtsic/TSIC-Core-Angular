namespace TSIC.Application.Services.Clubs;

/// <summary>
/// Pure business logic for fuzzy matching club names using Levenshtein distance.
/// </summary>
public static class ClubNameMatcher
{
    /// <summary>
    /// Normalizes club name for fuzzy matching:
    /// - Lowercase
    /// - Remove punctuation
    /// - Expand common abbreviations
    /// </summary>
    public static string NormalizeClubName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var normalized = name.ToLowerInvariant();

        // Common lacrosse/sports abbreviations
        normalized = normalized.Replace("lax", "lacrosse")
                               .Replace("lc", "lacrosse club")
                               .Replace("fc", "football club")
                               .Replace("sc", "soccer club")
                               .Replace("yc", "youth club");

        // Remove punctuation and extra spaces
        normalized = new string(normalized.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
        normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return normalized;
    }

    /// <summary>
    /// Calculate Levenshtein distance-based similarity percentage (0-100).
    /// Higher score means more similar.
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
    /// Determines if two club names are similar enough to be considered duplicates.
    /// </summary>
    public static bool AreSimilar(string name1, string name2, int threshold = 80)
    {
        var normalized1 = NormalizeClubName(name1);
        var normalized2 = NormalizeClubName(name2);
        return CalculateSimilarity(normalized1, normalized2) >= threshold;
    }

    /// <summary>
    /// Calculates the Levenshtein distance (edit distance) between two strings.
    /// Returns the minimum number of single-character edits required to change one string into the other.
    /// </summary>
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

