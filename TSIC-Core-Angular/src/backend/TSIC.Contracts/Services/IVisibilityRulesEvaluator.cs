namespace TSIC.Contracts.Services;

/// <summary>
/// Evaluates <c>NavItemVisibilityRules</c>-style JSON against job metadata.
/// Shared between nav rendering and reports-catalogue filtering so both
/// features apply identical gating rules with no drift.
///
/// Typical call pattern: build the context once per request, then evaluate
/// many rules against it.
/// </summary>
public interface IVisibilityRulesEvaluator
{
    /// <summary>
    /// Loads the job's sport / jobtype / customer / feature flags and stores
    /// the caller's roles for role-axis evaluation.
    /// Returns null if the job doesn't exist.
    /// </summary>
    Task<JobNavContext?> BuildJobContextAsync(
        Guid jobId,
        IEnumerable<string> callerRoles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when the rules allow visibility for this job context.
    /// Null/empty rulesJson is treated as "no rules" (always visible).
    /// Malformed JSON fails open (returns true) — matching the nav's behavior.
    /// </summary>
    bool Passes(string? rulesJson, JobNavContext context);
}

/// <summary>
/// Cached view of a job's visibility-relevant metadata + the caller's roles.
/// Built by <see cref="IVisibilityRulesEvaluator.BuildJobContextAsync"/>;
/// not exposed to the client.
/// </summary>
public sealed record JobNavContext(
    string? SportName,
    string? JobTypeName,
    string? CustomerName,
    HashSet<string> ActiveFlags,
    HashSet<string> CallerRoles);
