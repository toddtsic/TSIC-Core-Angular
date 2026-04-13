using System.Text.RegularExpressions;

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
    }

    /// <summary>All registered resolvers, in registration order.</summary>
    public IReadOnlyCollection<IBulletinTokenResolver> All => _resolvers.Values;

    public string ResolveTokens(string html, TokenContext ctx)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        return TokenRegex().Replace(html, match =>
        {
            var name = match.Groups[1].Value;
            return _resolvers.TryGetValue(name, out var resolver)
                ? resolver.Resolve(ctx)
                : match.Value;
        });
    }
}
