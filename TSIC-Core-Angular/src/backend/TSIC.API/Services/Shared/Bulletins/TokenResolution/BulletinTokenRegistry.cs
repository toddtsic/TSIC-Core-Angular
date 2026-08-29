using System.Text.RegularExpressions;
using TSIC.API.Services.Shared.TextSubstitution;

namespace TSIC.API.Services.Shared.Bulletins.TokenResolution;

/// <summary>
/// Walks bulletin HTML, replaces !TOKEN_NAME markers with resolver output.
/// Matches the project-wide !TOKEN convention (see TextSubstitutionService).
/// Unknown tokens are left untouched so authors can see them and fix typos.
/// </summary>
public sealed partial class BulletinTokenRegistry
{
    // Negative lookbehind prevents mid-word matches (e.g. "HURRY!REGISTER" should not resolve).
    // '!' must follow start-of-string, whitespace, or a non-alphanumeric character (tag boundary, punctuation).
    [GeneratedRegex(@"(?<![A-Za-z0-9])!([A-Z][A-Z0-9_]*)", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    private readonly Dictionary<string, IBulletinTokenResolver> _resolvers;

    public BulletinTokenRegistry(IEnumerable<IBulletinTokenResolver> resolvers)
    {
        _resolvers = resolvers.ToDictionary(r => r.TokenName, StringComparer.Ordinal);

        // Two sources of truth for one token is always a bug. Job tokens (JobTokens.Names) and
        // widget resolvers share this namespace; a collision would let a resolver silently
        // shadow a job value. Fail loudly rather than pick a winner.
        var collisions = JobTokens.Names.Where(_resolvers.ContainsKey).ToList();
        if (collisions.Count > 0)
        {
            throw new InvalidOperationException(
                $"Bulletin token name(s) served by both a resolver and JobTokens: {string.Join(", ", collisions)}.");
        }
    }

    /// <summary>All registered resolvers, in registration order.</summary>
    public IReadOnlyCollection<IBulletinTokenResolver> All => _resolvers.Values;

    /// <summary>
    /// Resolves widget tokens and, when supplied, job tokens in a SINGLE walk.
    /// One pass matters: Regex.Replace never re-scans what it just inserted, so a job value
    /// that happens to look like a token (a job named "SUMMER!BLAST") can never be misread.
    /// A second sequential substitution pass would have that hazard.
    /// </summary>
    /// <param name="ctx">
    /// Widget context. NULL when the job pulse could not be loaded — widget tokens then pass
    /// through verbatim, exactly as they did before, while job tokens still resolve. Job values
    /// must not depend on pulse availability: !JOBNAME resolved without it previously and a
    /// regression there would put a raw token on a public page.
    /// </param>
    /// <param name="jobTokens">
    /// Job-scoped values from ITextSubstitutionService.BuildJobTokensAsync, keyed without the
    /// leading '!'. Null on paths with no job context; those tokens then pass through verbatim,
    /// exactly like any unknown token.
    /// </param>
    public string ResolveTokens(string html, TokenContext? ctx, IReadOnlyDictionary<string, string>? jobTokens = null)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        return TokenRegex().Replace(html, match =>
        {
            var name = match.Groups[1].Value;
            if (ctx != null && _resolvers.TryGetValue(name, out var resolver))
            {
                return resolver.Resolve(ctx);
            }

            return jobTokens != null && jobTokens.TryGetValue(name, out var value)
                ? value
                : match.Value;
        });
    }
}
