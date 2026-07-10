namespace TSIC.Contracts.Dtos.MyRoster;

/// <summary>
/// Response from GET /api/my-roster. When Allowed=false, the frontend renders a
/// friendly alert; other fields will be null.
/// </summary>
public record MyRosterResponseDto
{
    public required bool Allowed { get; init; }
    public string? Reason { get; init; }
    public Guid? TeamId { get; init; }
    public string? TeamName { get; init; }

    /// <summary>
    /// The job's configured parent labels (Jobs.MomLabel / Jobs.DadLabel). The roster cards use
    /// these instead of hardcoding "Mom"/"Dad" so a job can say "Guardian", "Parent 1", etc.
    /// Fall back to "Mom"/"Dad" when a job leaves them unset.
    /// </summary>
    public string? MomLabel { get; init; }
    public string? DadLabel { get; init; }

    public List<MyRosterPlayerDto>? Players { get; init; }
}

/// <summary>
/// A single roster row for the Player/Staff "View Rosters" view — a parent- and player-facing
/// team directory. Carries the registrant's own contact plus Mom/Dad contact (from Families,
/// joined via Registrations.FamilyUserId) so the card can offer one-tap parent email/call.
/// Parent fields are null for adults/staff with no family account.
/// </summary>
public record MyRosterPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string RoleName { get; init; }
    public required bool BActive { get; init; }

    /// <summary>Registrant's own name parts — for natural "First Last" display and monogram initials.</summary>
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public int? GradYear { get; init; }
    public string? Position { get; init; }
    public string? Gender { get; init; }

    // Parent contacts (Families.Mom* / Families.Dad*). Any/all may be null.
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomCellphone { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadEmail { get; init; }
    public string? DadCellphone { get; init; }
}

/// <summary>
/// Request to send a batch email to caller's teammates.
/// If RegistrationIds is null/empty, the server emails the entire team roster.
/// Otherwise, server validates that every id is on the caller's team.
/// </summary>
public record MyRosterBatchEmailRequest
{
    public List<Guid>? RegistrationIds { get; init; }
    public required string Subject { get; init; }
    public required string BodyTemplate { get; init; }
}
