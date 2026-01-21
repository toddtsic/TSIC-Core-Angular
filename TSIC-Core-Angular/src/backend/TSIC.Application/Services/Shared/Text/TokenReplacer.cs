using System.Text;

namespace TSIC.Application.Services.Shared.Text;

/// <summary>
/// Pure business logic for text template token replacement.
/// Performs simple string substitution from a dictionary of tokens.
/// </summary>
public static class TokenReplacer
{
    /// <summary>
    /// Replaces all tokens in the template with their corresponding values.
    /// Tokens are case-sensitive string keys (e.g., "!JOBNAME", "!PERSON").
    /// Processes tokens in descending length order to avoid partial replacements.
    /// </summary>
    /// <param name="template">The template string containing tokens to replace.</param>
    /// <param name="tokens">Dictionary mapping token keys to replacement values.</param>
    /// <returns>The template with all tokens replaced by their values.</returns>
    public static string ReplaceTokens(string template, Dictionary<string, string> tokens)
    {
        var sb = new StringBuilder(template);
        // Sort tokens by length descending to replace longer tokens first
        // This prevents shorter tokens from matching parts of longer ones
        // e.g., !F-TEAMS won't match the "-TEAMS" in !F-ACCOUNTING-TEAMS
        foreach (var kvp in tokens.OrderByDescending(t => t.Key.Length))
        {
            sb.Replace(kvp.Key, kvp.Value);
        }
        return sb.ToString();
    }
}

