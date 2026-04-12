using System.Text.RegularExpressions;

namespace TSIC.API.Services.Shared.Bulletins.TokenResolution;

/// <summary>
/// Walks bulletin HTML, replaces {{TOKEN_NAME}} markers with resolver output.
/// Unknown tokens are left untouched so authors can see them and fix typos.
/// </summary>
public sealed partial class BulletinTokenRegistry
{
    [GeneratedRegex(@"\{\{([A-Z_]+)\}\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    private readonly Dictionary<string, IBulletinTokenResolver> _resolvers;

    public BulletinTokenRegistry(IEnumerable<IBulletinTokenResolver> resolvers)
    {
        _resolvers = resolvers.ToDictionary(r => r.TokenName, StringComparer.Ordinal);
    }

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
