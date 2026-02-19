namespace TSIC.Contracts.Dtos.JobConfig;

/// <summary>
/// Full job configuration for the Job Config Editor.
/// Maps 1:1 to Jobs entity configurable fields (~95 properties).
/// </summary>
public record JobConfigDto
{
    // ── Identity (read-only context) ──
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public required byte[]? UpdatedOn { get; init; }

    // ── General ──
    public required string? JobName { get; init; }
    public required string? DisplayName { get; init; }
    public required string? JobDescription { get; init; }
    public required string? JobTagline { get; init; }
    public required string? JobCode { get; init; }
    public required string? Year { get; init; }
    public required string? Season { get; init; }
    public required int JobTypeId { get; init; }
    public required Guid SportId { get; init; }
    public required int BillingTypeId { get; init; }
    public required DateTime ExpiryAdmin { get; init; }
    public required DateTime ExpiryUsers { get; init; }
    public required DateTime? EventStartDate { get; init; }
    public required DateTime? EventEndDate { get; init; }
    public required string? SearchenginKeywords { get; init; }
    public required string? SearchengineDescription { get; init; }
    public required bool BBannerIsCustom { get; init; }
    public required string? BannerFile { get; init; }
    public required string? MobileJobName { get; init; }
    public required string? JobNameQbp { get; init; }
    public required string? MomLabel { get; init; }
    public required string? DadLabel { get; init; }
    public required bool BSuspendPublic { get; init; }

    // ── Registration ──
    public required bool? BRegistrationAllowPlayer { get; init; }
    public required bool? BRegistrationAllowTeam { get; init; }
    public required bool? BAllowMobileRegn { get; init; }
    public required bool BUseWaitlists { get; init; }
    public required bool? BRestrictPlayerTeamsToAgerange { get; init; }
    public required bool? BOfferPlayerRegsaverInsurance { get; init; }
    public required bool? BOfferTeamRegsaverInsurance { get; init; }
    public required int? PlayerRegMultiPlayerDiscountMin { get; init; }
    public required int? PlayerRegMultiPlayerDiscountPercent { get; init; }
    public required string? CoreRegformPlayer { get; init; }
    public required string RegformNamePlayer { get; init; }
    public required string RegformNameTeam { get; init; }
    public required string RegformNameCoach { get; init; }
    public required string RegformNameClubRep { get; init; }
    public required string? PlayerProfileMetadataJson { get; init; }
    public required DateTime? UslaxNumberValidThroughDate { get; init; }

    // ── Payment ──
    public required int PaymentMethodsAllowedCode { get; init; }
    public required bool BAddProcessingFees { get; init; }
    public required decimal? ProcessingFeePercent { get; init; }
    public required bool? BApplyProcessingFeesToTeamDeposit { get; init; }
    public required bool? BTeamsFullPaymentRequired { get; init; }
    public required string? Balancedueaspercent { get; init; }
    public required bool? BAllowRefundsInPriorMonths { get; init; }
    public required bool? BAllowCreditAll { get; init; }
    public required string? PayTo { get; init; }
    public required string? MailTo { get; init; }
    public required string? MailinPaymentWarning { get; init; }
    public required bool? AdnArb { get; init; }
    public required int? AdnArbbillingOccurences { get; init; }
    public required int? AdnArbintervalLength { get; init; }
    public required DateTime? AdnArbstartDate { get; init; }
    public required decimal? AdnArbMinimunTotalCharge { get; init; }

    // ── Email & Templates ──
    public required string? RegFormFrom { get; init; }
    public required string? RegFormCcs { get; init; }
    public required string? RegFormBccs { get; init; }
    public required string? Rescheduleemaillist { get; init; }
    public required string? Alwayscopyemaillist { get; init; }
    public required bool? BDisallowCcplayerConfirmations { get; init; }
    public required string? PlayerRegConfirmationEmail { get; init; }
    public required string? PlayerRegConfirmationOnScreen { get; init; }
    public required string? PlayerRegRefundPolicy { get; init; }
    public required string? PlayerRegReleaseOfLiability { get; init; }
    public required string? PlayerRegCodeOfConduct { get; init; }
    public required string? PlayerRegCovid19Waiver { get; init; }
    public required string? AdultRegConfirmationEmail { get; init; }
    public required string? AdultRegConfirmationOnScreen { get; init; }
    public required string? AdultRegRefundPolicy { get; init; }
    public required string? AdultRegReleaseOfLiability { get; init; }
    public required string? AdultRegCodeOfConduct { get; init; }
    public required string? RefereeRegConfirmationEmail { get; init; }
    public required string? RefereeRegConfirmationOnScreen { get; init; }
    public required string? RecruiterRegConfirmationEmail { get; init; }
    public required string? RecruiterRegConfirmationOnScreen { get; init; }

    // ── Features & Store ──
    public required bool? BClubRepAllowEdit { get; init; }
    public required bool? BClubRepAllowDelete { get; init; }
    public required bool? BClubRepAllowAdd { get; init; }
    public required bool BAllowMobileLogin { get; init; }
    public required bool BAllowRosterViewAdult { get; init; }
    public required bool BAllowRosterViewPlayer { get; init; }
    public required bool BShowTeamNameOnlyInSchedules { get; init; }
    public required bool? BScheduleAllowPublicAccess { get; init; }
    public required bool? BTeamPushDirectors { get; init; }
    public required bool? BEnableTsicteams { get; init; }
    public required bool? BEnableMobileRsvp { get; init; }
    public required bool? BEnableMobileTeamChat { get; init; }
    public required bool? BenableStp { get; init; }
    public required bool? BEnableStore { get; init; }
    public required bool? BSignalRschedule { get; init; }
    public required int? MobileScoreHoursPastGameEligible { get; init; }
    public required decimal StoreSalesTax { get; init; }
    public required string? StoreRefundPolicy { get; init; }
    public required string? StorePickupDetails { get; init; }
    public required string? StoreContactEmail { get; init; }
    public required decimal? StoreTsicrate { get; init; }
}

/// <summary>
/// Update request for job configuration. Same shape as JobConfigDto
/// minus read-only JobPath. Includes UpdatedOn for concurrency check.
/// </summary>
public record UpdateJobConfigRequest
{
    public required Guid JobId { get; init; }
    public required byte[]? UpdatedOn { get; init; }

    // ── General ──
    public required string? JobName { get; init; }
    public required string? DisplayName { get; init; }
    public required string? JobDescription { get; init; }
    public required string? JobTagline { get; init; }
    public required string? JobCode { get; init; }
    public required string? Year { get; init; }
    public required string? Season { get; init; }
    public required int JobTypeId { get; init; }
    public required Guid SportId { get; init; }
    public required int BillingTypeId { get; init; }
    public required DateTime ExpiryAdmin { get; init; }
    public required DateTime ExpiryUsers { get; init; }
    public required DateTime? EventStartDate { get; init; }
    public required DateTime? EventEndDate { get; init; }
    public required string? SearchenginKeywords { get; init; }
    public required string? SearchengineDescription { get; init; }
    public required bool BBannerIsCustom { get; init; }
    public required string? BannerFile { get; init; }
    public required string? MobileJobName { get; init; }
    public required string? JobNameQbp { get; init; }
    public required string? MomLabel { get; init; }
    public required string? DadLabel { get; init; }
    public required bool BSuspendPublic { get; init; }

    // ── Registration ──
    public required bool? BRegistrationAllowPlayer { get; init; }
    public required bool? BRegistrationAllowTeam { get; init; }
    public required bool? BAllowMobileRegn { get; init; }
    public required bool BUseWaitlists { get; init; }
    public required bool? BRestrictPlayerTeamsToAgerange { get; init; }
    public required bool? BOfferPlayerRegsaverInsurance { get; init; }
    public required bool? BOfferTeamRegsaverInsurance { get; init; }
    public required int? PlayerRegMultiPlayerDiscountMin { get; init; }
    public required int? PlayerRegMultiPlayerDiscountPercent { get; init; }
    public required string? CoreRegformPlayer { get; init; }
    public required string RegformNamePlayer { get; init; }
    public required string RegformNameTeam { get; init; }
    public required string RegformNameCoach { get; init; }
    public required string RegformNameClubRep { get; init; }
    public required string? PlayerProfileMetadataJson { get; init; }
    public required DateTime? UslaxNumberValidThroughDate { get; init; }

    // ── Payment ──
    public required int PaymentMethodsAllowedCode { get; init; }
    public required bool BAddProcessingFees { get; init; }
    public required decimal? ProcessingFeePercent { get; init; }
    public required bool? BApplyProcessingFeesToTeamDeposit { get; init; }
    public required bool? BTeamsFullPaymentRequired { get; init; }
    public required string? Balancedueaspercent { get; init; }
    public required bool? BAllowRefundsInPriorMonths { get; init; }
    public required bool? BAllowCreditAll { get; init; }
    public required string? PayTo { get; init; }
    public required string? MailTo { get; init; }
    public required string? MailinPaymentWarning { get; init; }
    public required bool? AdnArb { get; init; }
    public required int? AdnArbbillingOccurences { get; init; }
    public required int? AdnArbintervalLength { get; init; }
    public required DateTime? AdnArbstartDate { get; init; }
    public required decimal? AdnArbMinimunTotalCharge { get; init; }

    // ── Email & Templates ──
    public required string? RegFormFrom { get; init; }
    public required string? RegFormCcs { get; init; }
    public required string? RegFormBccs { get; init; }
    public required string? Rescheduleemaillist { get; init; }
    public required string? Alwayscopyemaillist { get; init; }
    public required bool? BDisallowCcplayerConfirmations { get; init; }
    public required string? PlayerRegConfirmationEmail { get; init; }
    public required string? PlayerRegConfirmationOnScreen { get; init; }
    public required string? PlayerRegRefundPolicy { get; init; }
    public required string? PlayerRegReleaseOfLiability { get; init; }
    public required string? PlayerRegCodeOfConduct { get; init; }
    public required string? PlayerRegCovid19Waiver { get; init; }
    public required string? AdultRegConfirmationEmail { get; init; }
    public required string? AdultRegConfirmationOnScreen { get; init; }
    public required string? AdultRegRefundPolicy { get; init; }
    public required string? AdultRegReleaseOfLiability { get; init; }
    public required string? AdultRegCodeOfConduct { get; init; }
    public required string? RefereeRegConfirmationEmail { get; init; }
    public required string? RefereeRegConfirmationOnScreen { get; init; }
    public required string? RecruiterRegConfirmationEmail { get; init; }
    public required string? RecruiterRegConfirmationOnScreen { get; init; }

    // ── Features & Store ──
    public required bool? BClubRepAllowEdit { get; init; }
    public required bool? BClubRepAllowDelete { get; init; }
    public required bool? BClubRepAllowAdd { get; init; }
    public required bool BAllowMobileLogin { get; init; }
    public required bool BAllowRosterViewAdult { get; init; }
    public required bool BAllowRosterViewPlayer { get; init; }
    public required bool BShowTeamNameOnlyInSchedules { get; init; }
    public required bool? BScheduleAllowPublicAccess { get; init; }
    public required bool? BTeamPushDirectors { get; init; }
    public required bool? BEnableTsicteams { get; init; }
    public required bool? BEnableMobileRsvp { get; init; }
    public required bool? BEnableMobileTeamChat { get; init; }
    public required bool? BenableStp { get; init; }
    public required bool? BEnableStore { get; init; }
    public required bool? BSignalRschedule { get; init; }
    public required int? MobileScoreHoursPastGameEligible { get; init; }
    public required decimal StoreSalesTax { get; init; }
    public required string? StoreRefundPolicy { get; init; }
    public required string? StorePickupDetails { get; init; }
    public required string? StoreContactEmail { get; init; }
    public required decimal? StoreTsicrate { get; init; }
}

/// <summary>
/// Lookup data for dropdown selectors in the Job Config Editor.
/// </summary>
public record JobConfigLookupsDto
{
    public required List<JobTypeLookupDto> JobTypes { get; init; }
    public required List<SportLookupDto> Sports { get; init; }
    public required List<BillingTypeLookupDto> BillingTypes { get; init; }
}

public record JobTypeLookupDto
{
    public required int JobTypeId { get; init; }
    public required string? JobTypeName { get; init; }
}

public record SportLookupDto
{
    public required Guid SportId { get; init; }
    public required string? SportName { get; init; }
}

public record BillingTypeLookupDto
{
    public required int BillingTypeId { get; init; }
    public required string? BillingTypeName { get; init; }
}
