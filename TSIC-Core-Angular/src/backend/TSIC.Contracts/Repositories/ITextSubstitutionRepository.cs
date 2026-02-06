namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for text substitution service queries.
/// Consolidates complex cross-entity projections needed for token substitution.
/// </summary>
public interface ITextSubstitutionRepository
{
    /// <summary>
    /// Get job basic info for token substitution.
    /// </summary>
    Task<JobTokenInfo?> GetJobTokenInfoAsync(string jobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load fixed fields for a single registration.
    /// </summary>
    Task<List<FixedFieldsData>> LoadFixedFieldsByRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load fixed fields for all registrations in a family for a specific job.
    /// </summary>
    Task<List<FixedFieldsData>> LoadFixedFieldsByFamilyAsync(Guid jobId, string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get accounting transaction rows for a registration.
    /// </summary>
    Task<List<AccountingTransactionRow>> GetAccountingTransactionsAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team and club names for registrations.
    /// </summary>
    Task<Dictionary<Guid, string>> GetTeamClubNamesAsync(List<Guid> registrationIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get director contact info for a job.
    /// </summary>
    Task<DirectorContactData?> GetDirectorContactAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get username for a family user.
    /// </summary>
    Task<string?> GetFamilyUserNameAsync(string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team name with club for a team.
    /// </summary>
    Task<string?> GetTeamNameWithClubAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get age group and team name for a registration.
    /// </summary>
    Task<string?> GetAgeGroupPlusTeamNameAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get age group name for a team.
    /// </summary>
    Task<string?> GetAgeGroupNameAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get league name for a team.
    /// </summary>
    Task<string?> GetLeagueNameAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get club name for a club rep registration.
    /// </summary>
    Task<string?> GetClubNameAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get teams for a club rep registration (for accounting display).
    /// </summary>
    Task<List<ClubTeamInfo>> GetClubTeamsAsync(Guid clubRepRegistrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get accounting transactions for a specific team.
    /// </summary>
    Task<List<TeamAccountingRow>> GetTeamAccountingTransactionsAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get teams summary for a club rep registration.
    /// </summary>
    Task<List<TeamSummaryRow>> GetTeamsSummaryAsync(Guid clubRepRegistrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get simple team list for a club rep registration (no money).
    /// </summary>
    Task<List<SimpleTeamRow>> GetSimpleTeamsAsync(Guid clubRepRegistrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job waivers/policies.
    /// </summary>
    Task<JobWaiverData?> GetJobWaiversAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get staff registration info for choices display.
    /// </summary>
    Task<StaffRegistrationInfo?> GetStaffInfoAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get coach team choices for a registration.
    /// </summary>
    Task<List<CoachTeamChoice>> GetCoachTeamChoicesAsync(Guid registrationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Job info for simple token substitution.
/// </summary>
public record JobTokenInfo
{
    public required string JobName { get; init; }
    public DateTime? UslaxNumberValidThroughDate { get; init; }
}

/// <summary>
/// Fixed fields data for text substitution (comprehensive projection).
/// </summary>
public record FixedFieldsData
{
    public required Guid RegistrationId { get; init; }
    public required Guid JobId { get; init; }
    public string? FamilyUserId { get; init; }
    public string? Person { get; init; }
    public string? Assignment { get; init; }
    public string? UserName { get; init; }
    public decimal? FeeTotal { get; init; }
    public decimal? PaidTotal { get; init; }
    public decimal? OwedTotal { get; init; }
    public string? RegistrationCategory { get; init; }
    public string? ClubName { get; init; }
    public string? CustomerName { get; init; }
    public string? Email { get; init; }
    public string? JobDescription { get; init; }
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
    public string? MailTo { get; init; }
    public string? PayTo { get; init; }
    public string? RoleName { get; init; }
    public string? Season { get; init; }
    public string? SportName { get; init; }
    public Guid? AssignedTeamId { get; init; }
    public bool? Active { get; init; }
    public string? Volposition { get; init; }
    public string? UniformNo { get; init; }
    public string? DayGroup { get; init; }
    public string? JerseySize { get; init; }
    public string? ShortsSize { get; init; }
    public string? TShirtSize { get; init; }
    public required bool AdnArb { get; init; }
    public string? AdnSubscriptionId { get; init; }
    public string? AdnSubscriptionStatus { get; init; }
    public int? AdnSubscriptionBillingOccurences { get; init; }
    public decimal? AdnSubscriptionAmountPerOccurence { get; init; }
    public DateTime? AdnSubscriptionStartDate { get; init; }
    public int? AdnSubscriptionIntervalLength { get; init; }
    public string? JobLogoHeader { get; init; }
    public string? JobCode { get; init; }
    public DateTime? UslaxNumberValidThroughDate { get; init; }
}

/// <summary>
/// Accounting transaction row.
/// </summary>
public record AccountingTransactionRow
{
    public required int AId { get; init; }
    public required string RegistrantName { get; init; }
    public string? PaymentMethod { get; init; }
    public DateTime? Createdate { get; init; }
    public decimal? Payamt { get; init; }
    public decimal? Dueamt { get; init; }
    public int? DiscountCodeAi { get; init; }
    public required Guid PaymentMethodId { get; init; }
    public string? Comment { get; init; }
}

/// <summary>
/// Director contact data.
/// </summary>
public record DirectorContactData
{
    public required string Name { get; init; }
    public required string Email { get; init; }
}

/// <summary>
/// Club team info (for accounting tables).
/// </summary>
public record ClubTeamInfo
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
}

/// <summary>
/// Team accounting transaction row.
/// </summary>
public record TeamAccountingRow
{
    public bool? Active { get; init; }
    public required int AId { get; init; }
    public string? PaymentMethod { get; init; }
    public decimal? Dueamt { get; init; }
    public decimal? Payamt { get; init; }
    public DateTime? Createdate { get; init; }
    public string? Comment { get; init; }
    public int? DiscountCodeAi { get; init; }
    public required Guid PaymentMethodId { get; init; }
}

/// <summary>
/// Team summary row (financial summary).
/// </summary>
public record TeamSummaryRow
{
    public required string TeamName { get; init; }
    public decimal? FeeTotal { get; init; }
    public decimal? PaidTotal { get; init; }
    public decimal? OwedTotal { get; init; }
    public string? Dow { get; init; }
    public decimal? ProcessingFees { get; init; }
    public decimal? RosterFee { get; init; }
    public decimal? AdditionalFees { get; init; }
    public string? ClubName { get; init; }
}

/// <summary>
/// Simple team row (just name).
/// </summary>
public record SimpleTeamRow
{
    public required string TeamName { get; init; }
    public string? ClubName { get; init; }
}

/// <summary>
/// Job waiver/policy data.
/// </summary>
public record JobWaiverData
{
    public string? PlayerRegRefundPolicy { get; init; }
    public string? PlayerRegReleaseOfLiability { get; init; }
    public string? AdultRegReleaseOfLiability { get; init; }
    public string? PlayerRegCodeOfConduct { get; init; }
    public string? PlayerRegCovid19Waiver { get; init; }
}

/// <summary>
/// Staff registration info.
/// </summary>
public record StaffRegistrationInfo
{
    public required string UserId { get; init; }
    public required Guid JobId { get; init; }
    public string? SpecialRequests { get; init; }
}

/// <summary>
/// Coach team choice.
/// </summary>
public record CoachTeamChoice
{
    public string? Club { get; init; }
    public string? Age { get; init; }
    public string? Team { get; init; }
}
