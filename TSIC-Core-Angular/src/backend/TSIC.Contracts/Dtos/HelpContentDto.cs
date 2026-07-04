namespace TSIC.Contracts.Dtos;

/// <summary>
/// A single context-sensitive help fragment, keyed by component + topic
/// (file: <c>App_Data/Help/{component}/{topic}.html</c>).
/// </summary>
public record HelpContentDto
{
    public required string Component { get; init; }

    public required string Topic { get; init; }

    /// <summary>
    /// The authored HTML body, or <c>null</c> when no file exists yet — the client renders an
    /// "Under Development" state in that case.
    /// </summary>
    public string? Html { get; init; }

    /// <summary>True when a content file exists for this key.</summary>
    public required bool Exists { get; init; }

    /// <summary>
    /// True when this environment permits SuperUser in-app editing (sandbox only — Development +
    /// Staging). Production is read-only; edits are authored on staging, committed, and deployed (Model A).
    /// The client shows the pencil / edit affordance only when this is true.
    /// </summary>
    public required bool CanEdit { get; init; }
}

/// <summary>Body for a SuperUser help-content save (PUT /api/help/{component}/{topic}).</summary>
public record SaveHelpContentRequest
{
    public required string Html { get; init; }
}

/// <summary>
/// Which help pages actually have content, so the "?" launcher can hide itself where there's nothing
/// to show. Fetched once at app load. Keys are "component/topic".
/// </summary>
public record HelpManifestDto
{
    /// <summary>Every authored key ("component/topic") that has a content file.</summary>
    public required IReadOnlyList<string> Keys { get; init; }

    /// <summary>
    /// True when this environment permits editing (sandbox). Lets the launcher stay visible for a
    /// SuperUser on unwritten pages (the author-in-place on-ramp) while hiding it from everyone else.
    /// </summary>
    public required bool CanEdit { get; init; }
}
