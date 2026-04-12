namespace TSIC.Contracts.Dtos.Bulletin;

/// <summary>
/// Author-facing metadata for one {{TOKEN}} in the bulletin vocabulary.
/// Returned by GET /api/Bulletins/token-catalog.
/// </summary>
public sealed record BulletinTokenCatalogEntryDto
{
    /// <summary>Token name without braces (e.g. "REGISTER_PLAYER").</summary>
    public required string TokenName { get; init; }

    /// <summary>Human-readable description for the editor sidebar.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Names of JobPulseDto properties this token gates on (e.g. ["PlayerRegistrationOpen"]).
    /// Empty array = always visible.
    /// </summary>
    public required IReadOnlyList<string> GatingConditions { get; init; }

    /// <summary>
    /// Sample resolved HTML rendered under an all-flags-true demo pulse, for editor preview.
    /// </summary>
    public required string SampleHtml { get; init; }
}
