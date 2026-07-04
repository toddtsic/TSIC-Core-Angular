using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

/// <summary>
/// Reads and writes context-sensitive help content stored as HTML fragments under
/// <c>App_Data/Help/{component}/{topic}.html</c>. Reads are public; writes are SuperUser + sandbox only
/// (the working-tree file is the source of truth — edited on staging, committed, deployed: Model A).
/// </summary>
public interface IHelpContentService
{
    /// <summary>True when this environment permits editing (sandbox only, i.e. non-production).</summary>
    bool CanEdit { get; }

    /// <summary>
    /// Whether a component/topic path segment is safe — lowercase alphanumerics and hyphens only,
    /// ≤64 chars. Rejects anything that could escape the help directory (<c>.</c>, <c>/</c>, <c>\</c>).
    /// </summary>
    bool IsValidSegment(string? segment);

    /// <summary>List every authored key ("component/topic") that has a content file, plus the edit flag.</summary>
    HelpManifestDto GetManifest();

    /// <summary>Load the fragment for a key. Returns <see cref="HelpContentDto.Exists"/> = false when unauthored.</summary>
    Task<HelpContentDto> GetAsync(string component, string topic, CancellationToken ct = default);

    /// <summary>Write (create or overwrite) the fragment atomically. Caller must have already gated on <see cref="CanEdit"/>.</summary>
    Task<HelpContentDto> SaveAsync(string component, string topic, string html, CancellationToken ct = default);
}
