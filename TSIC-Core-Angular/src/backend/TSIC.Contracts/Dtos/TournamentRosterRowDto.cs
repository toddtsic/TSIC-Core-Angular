namespace TSIC.Contracts.Dtos;

/// <summary>
/// One raw per-registrant row for the tournament roster family (packed roster + recruiter
/// report). This is the EF-query replacement for the <c>reporting_migrate.TournamentRosterPacked_Flat</c>
/// stored proc: it returns the <em>unshaped</em> superset of columns and lets the PDF layer do
/// all display shaping in C# (UPPER team names, "Club Rep" placeholder suppression, phone
/// formatting, the staff→school_name phone overload, college-commit nulling, SAT computation,
/// and the email/address/phone family fallbacks).
///
/// Moving the projection from the proc into an EF query collapses the field set into one place:
/// because the field keys are already mirrored in C# (the Designer's field pool, the row
/// mapper, and the cell resolver), editing the proc never actually avoided a code change.
/// </summary>
public record TournamentRosterRowDto
{
    // ── Grouping / team identity (always present) ──────────────────────────────
    public required Guid TeamId { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string TeamName { get; init; }

    // ── Club rep (raw; C# suppresses the dummy "Club Rep" user + formats phone) ──
    /// <summary>Club name off the club-rep registration; null when no rep assigned.</summary>
    public string? ClubName { get; init; }
    public string? ClubRepFirstName { get; init; }
    public string? ClubRepLastName { get; init; }
    public string? ClubRepEmail { get; init; }
    public string? ClubRepCellphone { get; init; }

    // ── Player / staff identity + academics ────────────────────────────────────
    public string? RoleName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? UniformNo { get; init; }
    public string? Position { get; init; }
    public string? SchoolName { get; init; }
    public string? GradYear { get; init; }
    public string? Gpa { get; init; }
    public bool? BCollegeCommit { get; init; }
    public string? CollegeCommit { get; init; }

    /// <summary>The registrant's own cellphone (staff rows surface this in the school column).</summary>
    public string? Cellphone { get; init; }

    // ── Recruiter contact: player's own fields ─────────────────────────────────
    public string? PlayerEmail { get; init; }
    public string? PlayerStreet { get; init; }
    public string? PlayerCity { get; init; }
    public string? PlayerState { get; init; }
    public string? PlayerZip { get; init; }

    // ── Recruiter contact: family-account fallback (uF = AspNetUsers @ FamilyUserId) ──
    public string? FamilyEmail { get; init; }
    public string? FamilyStreet { get; init; }
    public string? FamilyCity { get; init; }
    public string? FamilyState { get; init; }
    public string? FamilyZip { get; init; }

    /// <summary>Mom cellphone (Families) — last fallback for the contact phone.</summary>
    public string? MomCellphone { get; init; }

    // ── Recruiter academics: SAT components (summed in C#) ─────────────────────
    public string? SatMath { get; init; }
    public string? SatVerbal { get; init; }
    public string? SatWriting { get; init; }
}
