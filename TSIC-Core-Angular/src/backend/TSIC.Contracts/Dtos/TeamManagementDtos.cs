namespace TSIC.Contracts.Dtos;

// ══════════════════════════════════════════════════════════════════════
// Team Roster
// ══════════════════════════════════════════════════════════════════════

public record TeamRosterDetailDto
{
    public required List<TeamRosterStaffDto> Staff { get; init; }
    public required List<TeamRosterPlayerDto> Players { get; init; }
}

public record TeamRosterStaffDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Cellphone { get; init; }
    public string? Email { get; init; }
    public string? HeadshotUrl { get; init; }
    public string? UserName { get; init; }
    public string? UserId { get; init; }
}

public record TeamRosterPlayerDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? RoleName { get; init; }
    public string? Cellphone { get; init; }
    public string? Email { get; init; }
    public string? HeadshotUrl { get; init; }
    public string? Mom { get; init; }
    public string? MomEmail { get; init; }
    public string? MomCellphone { get; init; }
    public string? Dad { get; init; }
    public string? DadEmail { get; init; }
    public string? DadCellphone { get; init; }
    public string? UniformNumber { get; init; }
    public string? City { get; init; }
    public string? School { get; init; }
    public string? UserName { get; init; }
    public string? UserId { get; init; }
    public int CountPresent { get; init; }
    public int CountNotPresent { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Team Links
// ══════════════════════════════════════════════════════════════════════

public record TeamLinkDto
{
    public required Guid DocId { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? JobId { get; init; }
    public required string Label { get; init; }
    public required string DocUrl { get; init; }
    public string? User { get; init; }
    public string? UserId { get; init; }
    public DateTime CreateDate { get; init; }
}

public record AddTeamLinkRequest
{
    public required string Label { get; init; }
    public required string DocUrl { get; init; }
    /// <summary>When true, creates a job-level link visible to all teams.</summary>
    public bool AddAllTeams { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Team Pushes
// ══════════════════════════════════════════════════════════════════════

public record TeamPushDto
{
    public required Guid Id { get; init; }
    public required Guid JobId { get; init; }
    public Guid? TeamId { get; init; }
    public string? UserId { get; init; }
    public string? User { get; init; }
    public required string PushText { get; init; }
    public required int DeviceCount { get; init; }
    public bool AddAllTeams { get; init; }
    public DateTime CreateDate { get; init; }
}

public record SendTeamPushRequest
{
    public required string PushText { get; init; }
    /// <summary>When true, sends push to all teams in the job.</summary>
    public bool AddAllTeams { get; init; }
}
