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
public record JobTokenInfo(
    string JobName,
    DateTime? UslaxNumberValidThroughDate);

/// <summary>
/// Fixed fields data for text substitution (comprehensive projection).
/// </summary>
public record FixedFieldsData(
    Guid RegistrationId,
    Guid JobId,
    string? FamilyUserId,
    string? Person,
    string? Assignment,
    string? UserName,
    decimal? FeeTotal,
    decimal? PaidTotal,
    decimal? OwedTotal,
    string? RegistrationCategory,
    string? ClubName,
    string? CustomerName,
    string? Email,
    string? JobDescription,
    string JobName,
    string JobPath,
    string? MailTo,
    string? PayTo,
    string? RoleName,
    string? Season,
    string? SportName,
    Guid? AssignedTeamId,
    bool? Active,
    string? Volposition,
    string? UniformNo,
    string? DayGroup,
    string? JerseySize,
    string? ShortsSize,
    string? TShirtSize,
    bool AdnArb,
    string? AdnSubscriptionId,
    string? AdnSubscriptionStatus,
    int? AdnSubscriptionBillingOccurences,
    decimal? AdnSubscriptionAmountPerOccurence,
    DateTime? AdnSubscriptionStartDate,
    int? AdnSubscriptionIntervalLength,
    string? JobLogoHeader,
    string? JobCode,
    DateTime? UslaxNumberValidThroughDate);

/// <summary>
/// Accounting transaction row.
/// </summary>
public record AccountingTransactionRow(
    int AId,
    string RegistrantName,
    string? PaymentMethod,
    DateTime? Createdate,
    decimal? Payamt,
    decimal? Dueamt,
    int? DiscountCodeAi,
    Guid PaymentMethodId,
    string? Comment);

/// <summary>
/// Director contact data.
/// </summary>
public record DirectorContactData(
    string Name,
    string Email);

/// <summary>
/// Club team info (for accounting tables).
/// </summary>
public record ClubTeamInfo(
    Guid TeamId,
    string TeamName);

/// <summary>
/// Team accounting transaction row.
/// </summary>
public record TeamAccountingRow(
    bool? Active,
    int AId,
    string? PaymentMethod,
    decimal? Dueamt,
    decimal? Payamt,
    DateTime? Createdate,
    string? Comment,
    int? DiscountCodeAi,
    Guid PaymentMethodId);

/// <summary>
/// Team summary row (financial summary).
/// </summary>
public record TeamSummaryRow(
    string TeamName,
    decimal? FeeTotal,
    decimal? PaidTotal,
    decimal? OwedTotal,
    string? Dow,
    decimal? ProcessingFees,
    decimal? RosterFee,
    decimal? AdditionalFees,
    string? ClubName);

/// <summary>
/// Simple team row (just name).
/// </summary>
public record SimpleTeamRow(
    string TeamName,
    string? ClubName);

/// <summary>
/// Job waiver/policy data.
/// </summary>
public record JobWaiverData(
    string? PlayerRegRefundPolicy,
    string? PlayerRegReleaseOfLiability,
    string? AdultRegReleaseOfLiability,
    string? PlayerRegCodeOfConduct,
    string? PlayerRegCovid19Waiver);

/// <summary>
/// Staff registration info.
/// </summary>
public record StaffRegistrationInfo(
    string UserId,
    Guid JobId,
    string? SpecialRequests);

/// <summary>
/// Coach team choice.
/// </summary>
public record CoachTeamChoice(
    string? Club,
    string? Age,
    string? Team);
