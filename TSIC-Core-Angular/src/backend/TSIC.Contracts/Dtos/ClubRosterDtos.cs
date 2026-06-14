namespace TSIC.Contracts.Dtos;

/// <summary>
/// One registrant row for the "Coaches Eyes Only" club roster — the EF replacement for the
/// legacy Crystal family <c>Job_Club_Rosters</c> (per-job, w/ medical), <c>Job_Rosters_NoMedical</c> /
/// <c>clubrostersNoMedicalII</c> (per-job, no medical), and <c>Club_AllJobs_Rosters_NoMedical</c>
/// (customer-scoped, no medical — every job of the requesting job's customer). The same render
/// drives all four; scope (single job vs all-customer-jobs) is the only data difference, and the
/// medical note is shown or withheld per the <c>includeMedical</c> flag.
///
/// Rows are grouped into one boxed "TEAM ROSTER:" block per assigned team. The all-jobs variant
/// carries <see cref="JobName"/> so the team header tells the coach which job a team belongs to.
/// </summary>
public record ClubRosterRowDto
{
    public required Guid RegistrationId { get; init; }

    // ── Team grouping / header composition ──
    /// <summary>Team identity = the group key (one boxed block per team).</summary>
    public required Guid TeamId { get; init; }
    public string? CustomerName { get; init; }
    public string? JobName { get; init; }
    public string? LeagueName { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? TeamName { get; init; }
    /// <summary>Club-rep club name (denormalized on the registration), shown in the team header when present.</summary>
    public string? ClubName { get; init; }

    // ── Player (Player column: name + email) ──
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }

    // ── Dob/Psn column ──
    public DateTime? Dob { get; init; }
    public string? Position { get; init; }

    // ── Phone/Sch column ──
    public string? Cellphone { get; init; }
    public string? SchoolName { get; init; }

    // ── Amt Due column (sensitive pay status) ──
    public decimal OwedTotal { get; init; }
    public decimal PaidTotal { get; init; }

    // ── Contacts column (Primary = Mom, Secondary = Dad) ──
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomCellphone { get; init; }
    public string? MomEmail { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadCellphone { get; init; }
    public string? DadEmail { get; init; }

    // ── Medical (shown only when includeMedical) ──
    public string? MedicalNote { get; init; }
}
