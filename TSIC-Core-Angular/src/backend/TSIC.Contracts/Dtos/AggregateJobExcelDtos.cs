namespace TSIC.Contracts.Dtos;

/// <summary>
/// Mobile "favourite team" counts for the push-results summary export — the EF replacement for
/// the <c>reporting.JobPushTeamResultsSummary</c> proc, which returns two sheets: the job total
/// and the per-team breakdown.
/// </summary>
public record JobPushTeamSummaryDto
{
    public required int TotalFavorites { get; init; }
    public required List<JobPushTeamCountExcelRowDto> Teams { get; init; }
}

/// <summary>One team's favourite count; club is null when the team has no club rep.</summary>
public record JobPushTeamCountExcelRowDto
{
    public string? ClubName { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public required int FavoriteCount { get; init; }
}

/// <summary>
/// One distinct team contact (club rep or staff) for the "Team Contacts" export — the EF
/// replacement for the <c>reporting.TournamentExportClubrepsAndCoachesEmail</c> proc. Job-scoped.
/// </summary>
public record TeamContactExcelRowDto
{
    public string? AgegroupName { get; init; }
    public string? ClubName { get; init; }
    public string? TeamName { get; init; }
    public string? Role { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Email { get; init; }
}

/// <summary>
/// One distinct player contact across all of a customer's jobs for the "Job Users" export — the
/// EF replacement for the <c>reporting.TLCContactsHx</c> proc. Customer-scoped.
/// </summary>
public record CustomerPlayerContactExcelRowDto
{
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Email { get; init; }
    public string? MomEmail { get; init; }
    public string? DadEmail { get; init; }
    public string? Cellphone { get; init; }
    public string? MomCellphone { get; init; }
    public string? DadCellphone { get; init; }
}

/// <summary>
/// One player registration with its fee breakdown across a customer's camp jobs of one year, for
/// the camp breakout "Players" export — the EF replacement for the
/// <c>reporting.CathyCampBreakoutPerJob_AmtPaid</c> proc. Customer-and-year-scoped, active or not.
/// </summary>
public record CampBreakoutPaidExcelRowDto
{
    public string? JobName { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public bool? Active { get; init; }
    public string? ClubTeamName { get; init; }
    public string? GradYear { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
}

/// <summary>
/// Active-player count for one job, year, agegroup and team of a customer — the detail level of
/// the "Counts Per Team" rollup export, the EF replacement for the
/// <c>reporting.AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam</c> proc. The
/// subtotal rows of the proc's <c>WITH ROLLUP</c> are added by the renderer.
/// </summary>
public record CustomerTeamCountDto
{
    public string? JobName { get; init; }
    public string? Year { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public required int PlayerCount { get; init; }
}

/// <summary>
/// Year-to-date player count for one camp year and team of the MaxExposure customer, for its
/// "Counts Per Team" export — the EF replacement for the
/// <c>utility.MaxExposure_CountsAtYear_ByTeam</c> proc. Not job-scoped.
/// </summary>
public record MaxExposureTeamCountExcelRowDto
{
    public string? Year { get; init; }
    public string? CustomerName { get; init; }
    public string? TeamName { get; init; }
    public required int RegistrantsToDate { get; init; }
}
