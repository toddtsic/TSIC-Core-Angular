using TSIC.Domain.JobRules;

namespace TSIC.Contracts.Dtos.JobConfig;

/// <summary>
/// The raw facts the readiness readout is evaluated over, straight from the DB.
///
/// Carries the Domain records rather than a third flat shape: <see cref="RegistrationReadiness"/>
/// already defines exactly what its two entry points consume, and re-declaring those fields here
/// would be one more place for the rule to drift. The repository fills them; the service calls
/// Compose/Describe; nobody re-derives.
/// </summary>
public record RegistrationReadinessFacts
{
    public required RegistrationReadiness.CoreFacts Core { get; init; }
    public required RegistrationReadiness.DescribeFacts Describe { get; init; }
}

/// <summary>One clause of the registration-visibility predicate, rendered for the admin UI.</summary>
public record ReadinessClauseDto
{
    /// <summary>Stable key — drives the UI's icon, anchor and fix-link routing.</summary>
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required bool Passed { get; init; }

    /// <summary>The evidence: real dates and counts, not a restatement of the label.</summary>
    public required string Detail { get; init; }

    /// <summary>
    /// Screen that fixes a failing clause — <c>scheduling</c>, <c>toggle</c>, <c>fees</c>,
    /// <c>teams</c> — or null when the clause is explanatory with no local fix.
    /// </summary>
    public string? FixTarget { get; init; }
}

/// <summary>
/// "Why isn't my registration link showing?", answered clause by clause.
///
/// Every clause here was already being enforced; none of it is new policy. What is new is that
/// the answer leaves the server — the director used to get a boolean and a silent public page.
/// </summary>
public record RegistrationReadinessDto
{
    /// <summary>True when the public "Register Player" card can render right now.</summary>
    public required bool PlayerCardVisible { get; init; }

    /// <summary>True when the public "Register a Team" card can render right now.</summary>
    public required bool TeamCardVisible { get; init; }

    public IReadOnlyList<ReadinessClauseDto> PlayerClauses { get; init; } = [];
    public IReadOnlyList<ReadinessClauseDto> TeamClauses { get; init; } = [];
}
