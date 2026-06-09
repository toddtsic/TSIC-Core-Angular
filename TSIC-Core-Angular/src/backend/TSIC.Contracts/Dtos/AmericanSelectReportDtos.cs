namespace TSIC.Contracts.Dtos;

/// <summary>
/// One tryout-player row for the American Select Evaluation sheet — the EF replacement for the
/// <c>reporting.AmericanSelectPlayerData</c> proc (legacy Crystal "AmericanSelectEvaluation").
/// Job-scoped, Player role, active, on a team whose name contains "Tryout". Inner-joined to
/// <c>Families</c> (mom contact), so registrants with no family record are excluded — matching the
/// proc. City is the <em>player's</em> city. The "Check In" column is a blank hand-entry box.
/// </summary>
public record AmericanSelectEvaluationRowDto
{
    public required Guid RegistrationId { get; init; }
    public string? JobName { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public string? UniformNo { get; init; }
    public string? GradYear { get; init; }
    public string? Position { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? ClubTeamName { get; init; }
    public string? SchoolName { get; init; }
    public string? City { get; init; }
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomCellphone { get; init; }
}
