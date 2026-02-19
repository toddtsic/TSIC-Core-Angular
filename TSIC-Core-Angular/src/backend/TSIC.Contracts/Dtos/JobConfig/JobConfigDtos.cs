namespace TSIC.Contracts.Dtos.JobConfig;

// ════════════════════════════════════════════════════════════════
// Full Config Wrapper (single GET returns all 8 categories)
// ════════════════════════════════════════════════════════════════

public record JobConfigFullDto
{
    public required JobConfigGeneralDto General { get; init; }
    public required JobConfigPaymentDto Payment { get; init; }
    public required JobConfigCommunicationsDto Communications { get; init; }
    public required JobConfigPlayerDto Player { get; init; }
    public required JobConfigTeamsDto Teams { get; init; }
    public required JobConfigCoachesDto Coaches { get; init; }
    public required JobConfigSchedulingDto Scheduling { get; init; }
    public required JobConfigMobileStoreDto MobileStore { get; init; }
}

// ════════════════════════════════════════════════════════════════
// 1. General
// ════════════════════════════════════════════════════════════════

public record JobConfigGeneralDto
{
    // Admin-visible
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public required string? JobName { get; init; }
    public required string? JobDescription { get; init; }
    public required string? JobTagline { get; init; }
    public required string? Season { get; init; }
    public required string? Year { get; init; }
    public required DateTime ExpiryUsers { get; init; }
    public required string? DisplayName { get; init; }
    public required string? SearchenginKeywords { get; init; }
    public required string? SearchengineDescription { get; init; }
    public required bool BBannerIsCustom { get; init; }
    public required string? BannerFile { get; init; }

    // SuperUser-only (null for non-super callers)
    public string? JobNameQbp { get; init; }
    public DateTime? ExpiryAdmin { get; init; }
    public int? JobTypeId { get; init; }
    public Guid? SportId { get; init; }
    public Guid? CustomerId { get; init; }
    public int? BillingTypeId { get; init; }
    public bool? BSuspendPublic { get; init; }
    public string? JobCode { get; init; }
}

public record UpdateJobConfigGeneralRequest
{
    public required string? JobName { get; init; }
    public required string? JobDescription { get; init; }
    public required string? JobTagline { get; init; }
    public required string? Season { get; init; }
    public required string? Year { get; init; }
    public required DateTime ExpiryUsers { get; init; }
    public required string? DisplayName { get; init; }
    public required string? SearchenginKeywords { get; init; }
    public required string? SearchengineDescription { get; init; }
    public required bool BBannerIsCustom { get; init; }
    public required string? BannerFile { get; init; }

    // SuperUser-only (ignored for non-super callers)
    public string? JobNameQbp { get; init; }
    public DateTime? ExpiryAdmin { get; init; }
    public int? JobTypeId { get; init; }
    public Guid? SportId { get; init; }
    public Guid? CustomerId { get; init; }
    public int? BillingTypeId { get; init; }
    public bool? BSuspendPublic { get; init; }
    public string? JobCode { get; init; }
}

// ════════════════════════════════════════════════════════════════
// 2. Payment & Billing
// ════════════════════════════════════════════════════════════════

public record JobConfigPaymentDto
{
    // Admin-visible
    public required int PaymentMethodsAllowedCode { get; init; }
    public required bool BAddProcessingFees { get; init; }
    public required decimal? ProcessingFeePercent { get; init; }
    public required bool? BApplyProcessingFeesToTeamDeposit { get; init; }
    public required decimal? PerPlayerCharge { get; init; }
    public required decimal? PerTeamCharge { get; init; }
    public required decimal? PerMonthCharge { get; init; }
    public required string? PayTo { get; init; }
    public required string? MailTo { get; init; }
    public required string? MailinPaymentWarning { get; init; }
    public required string? Balancedueaspercent { get; init; }
    public required bool? BTeamsFullPaymentRequired { get; init; }
    public required bool? BAllowRefundsInPriorMonths { get; init; }
    public required bool? BAllowCreditAll { get; init; }

    // SuperUser-only — ARB settings
    public bool? AdnArb { get; init; }
    public int? AdnArbBillingOccurrences { get; init; }
    public int? AdnArbIntervalLength { get; init; }
    public DateTime? AdnArbStartDate { get; init; }
    public decimal? AdnArbMinimumTotalCharge { get; init; }

    // SuperUser-only — admin charges summary
    public List<JobAdminChargeDto>? AdminCharges { get; init; }
}

public record UpdateJobConfigPaymentRequest
{
    public required int PaymentMethodsAllowedCode { get; init; }
    public required bool BAddProcessingFees { get; init; }
    public required decimal? ProcessingFeePercent { get; init; }
    public required bool? BApplyProcessingFeesToTeamDeposit { get; init; }
    public required decimal? PerPlayerCharge { get; init; }
    public required decimal? PerTeamCharge { get; init; }
    public required decimal? PerMonthCharge { get; init; }
    public required string? PayTo { get; init; }
    public required string? MailTo { get; init; }
    public required string? MailinPaymentWarning { get; init; }
    public required string? Balancedueaspercent { get; init; }
    public required bool? BTeamsFullPaymentRequired { get; init; }
    public required bool? BAllowRefundsInPriorMonths { get; init; }
    public required bool? BAllowCreditAll { get; init; }

    // SuperUser-only (ignored for non-super callers)
    public bool? AdnArb { get; init; }
    public int? AdnArbBillingOccurrences { get; init; }
    public int? AdnArbIntervalLength { get; init; }
    public DateTime? AdnArbStartDate { get; init; }
    public decimal? AdnArbMinimumTotalCharge { get; init; }
}

public record JobAdminChargeDto
{
    public required int Id { get; init; }
    public required int ChargeTypeId { get; init; }
    public required string? ChargeTypeName { get; init; }
    public required decimal ChargeAmount { get; init; }
    public required string? Comment { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
}

public record CreateAdminChargeRequest
{
    public required int ChargeTypeId { get; init; }
    public required decimal ChargeAmount { get; init; }
    public string? Comment { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
}

// ════════════════════════════════════════════════════════════════
// 3. Communications
// ════════════════════════════════════════════════════════════════

public record JobConfigCommunicationsDto
{
    public required string? DisplayName { get; init; }
    public required string? RegFormFrom { get; init; }
    public required string? RegFormCcs { get; init; }
    public required string? RegFormBccs { get; init; }
    public required string? Rescheduleemaillist { get; init; }
    public required string? Alwayscopyemaillist { get; init; }
    public required bool? BDisallowCcplayerConfirmations { get; init; }
}

public record UpdateJobConfigCommunicationsRequest
{
    public required string? DisplayName { get; init; }
    public required string? RegFormFrom { get; init; }
    public required string? RegFormCcs { get; init; }
    public required string? RegFormBccs { get; init; }
    public required string? Rescheduleemaillist { get; init; }
    public required string? Alwayscopyemaillist { get; init; }
    public required bool? BDisallowCcplayerConfirmations { get; init; }
}

// ════════════════════════════════════════════════════════════════
// 4. Player Registration
// ════════════════════════════════════════════════════════════════

public record JobConfigPlayerDto
{
    public required bool? BRegistrationAllowPlayer { get; init; }
    public required string RegformNamePlayer { get; init; }
    public required string? CoreRegformPlayer { get; init; }
    public required string? PlayerRegConfirmationEmail { get; init; }
    public required string? PlayerRegConfirmationOnScreen { get; init; }
    public required string? PlayerRegRefundPolicy { get; init; }
    public required string? PlayerRegReleaseOfLiability { get; init; }
    public required string? PlayerRegCodeOfConduct { get; init; }
    public required string? PlayerRegCovid19Waiver { get; init; }
    public required int? PlayerRegMultiPlayerDiscountMin { get; init; }
    public required int? PlayerRegMultiPlayerDiscountPercent { get; init; }

    // SuperUser-only
    public bool? BOfferPlayerRegsaverInsurance { get; init; }
    public string? MomLabel { get; init; }
    public string? DadLabel { get; init; }
    public string? PlayerProfileMetadataJson { get; init; }
}

public record UpdateJobConfigPlayerRequest
{
    public required bool? BRegistrationAllowPlayer { get; init; }
    public required string RegformNamePlayer { get; init; }
    public required string? CoreRegformPlayer { get; init; }
    public required string? PlayerRegConfirmationEmail { get; init; }
    public required string? PlayerRegConfirmationOnScreen { get; init; }
    public required string? PlayerRegRefundPolicy { get; init; }
    public required string? PlayerRegReleaseOfLiability { get; init; }
    public required string? PlayerRegCodeOfConduct { get; init; }
    public required string? PlayerRegCovid19Waiver { get; init; }
    public required int? PlayerRegMultiPlayerDiscountMin { get; init; }
    public required int? PlayerRegMultiPlayerDiscountPercent { get; init; }

    // SuperUser-only (ignored for non-super callers)
    public bool? BOfferPlayerRegsaverInsurance { get; init; }
    public string? MomLabel { get; init; }
    public string? DadLabel { get; init; }
    public string? PlayerProfileMetadataJson { get; init; }
}

// ════════════════════════════════════════════════════════════════
// 5. Teams & Club Reps
// ════════════════════════════════════════════════════════════════

public record JobConfigTeamsDto
{
    public required bool? BRegistrationAllowTeam { get; init; }
    public required string RegformNameTeam { get; init; }
    public required string RegformNameClubRep { get; init; }
    public required bool? BClubRepAllowEdit { get; init; }
    public required bool? BClubRepAllowDelete { get; init; }
    public required bool? BClubRepAllowAdd { get; init; }
    public required bool? BRestrictPlayerTeamsToAgerange { get; init; }
    public required bool? BTeamPushDirectors { get; init; }
    public required bool BUseWaitlists { get; init; }
    public required bool BShowTeamNameOnlyInSchedules { get; init; }

    // SuperUser-only
    public bool? BOfferTeamRegsaverInsurance { get; init; }
}

public record UpdateJobConfigTeamsRequest
{
    public required bool? BRegistrationAllowTeam { get; init; }
    public required string RegformNameTeam { get; init; }
    public required string RegformNameClubRep { get; init; }
    public required bool? BClubRepAllowEdit { get; init; }
    public required bool? BClubRepAllowDelete { get; init; }
    public required bool? BClubRepAllowAdd { get; init; }
    public required bool? BRestrictPlayerTeamsToAgerange { get; init; }
    public required bool? BTeamPushDirectors { get; init; }
    public required bool BUseWaitlists { get; init; }
    public required bool BShowTeamNameOnlyInSchedules { get; init; }

    // SuperUser-only (ignored for non-super callers)
    public bool? BOfferTeamRegsaverInsurance { get; init; }
}

// ════════════════════════════════════════════════════════════════
// 6. Coaches & Staff
// ════════════════════════════════════════════════════════════════

public record JobConfigCoachesDto
{
    public required string RegformNameCoach { get; init; }
    public required string? AdultRegConfirmationEmail { get; init; }
    public required string? AdultRegConfirmationOnScreen { get; init; }
    public required string? AdultRegRefundPolicy { get; init; }
    public required string? AdultRegReleaseOfLiability { get; init; }
    public required string? AdultRegCodeOfConduct { get; init; }
    public required string? RefereeRegConfirmationEmail { get; init; }
    public required string? RefereeRegConfirmationOnScreen { get; init; }
    public required string? RecruiterRegConfirmationEmail { get; init; }
    public required string? RecruiterRegConfirmationOnScreen { get; init; }
    public required bool BAllowRosterViewAdult { get; init; }
    public required bool BAllowRosterViewPlayer { get; init; }
}

public record UpdateJobConfigCoachesRequest
{
    public required string RegformNameCoach { get; init; }
    public required string? AdultRegConfirmationEmail { get; init; }
    public required string? AdultRegConfirmationOnScreen { get; init; }
    public required string? AdultRegRefundPolicy { get; init; }
    public required string? AdultRegReleaseOfLiability { get; init; }
    public required string? AdultRegCodeOfConduct { get; init; }
    public required string? RefereeRegConfirmationEmail { get; init; }
    public required string? RefereeRegConfirmationOnScreen { get; init; }
    public required string? RecruiterRegConfirmationEmail { get; init; }
    public required string? RecruiterRegConfirmationOnScreen { get; init; }
    public required bool BAllowRosterViewAdult { get; init; }
    public required bool BAllowRosterViewPlayer { get; init; }
}

// ════════════════════════════════════════════════════════════════
// 7. Scheduling
// ════════════════════════════════════════════════════════════════

public record JobConfigSchedulingDto
{
    public required DateTime? EventStartDate { get; init; }
    public required DateTime? EventEndDate { get; init; }
    public required bool? BScheduleAllowPublicAccess { get; init; }
    public required GameClockParamsDto? GameClock { get; init; }
}

public record UpdateJobConfigSchedulingRequest
{
    public required DateTime? EventStartDate { get; init; }
    public required DateTime? EventEndDate { get; init; }
    public required bool? BScheduleAllowPublicAccess { get; init; }
    public GameClockParamsDto? GameClock { get; init; }
}

public record GameClockParamsDto
{
    public required int Id { get; init; }
    public required decimal HalfMinutes { get; init; }
    public required decimal HalfTimeMinutes { get; init; }
    public required decimal TransitionMinutes { get; init; }
    public required decimal PlayoffMinutes { get; init; }
    public decimal? PlayoffHalfMinutes { get; init; }
    public decimal? PlayoffHalfTimeMinutes { get; init; }
    public decimal? QuarterMinutes { get; init; }
    public decimal? QuarterTimeMinutes { get; init; }
    public int? UtcOffsetHours { get; init; }
}

// ════════════════════════════════════════════════════════════════
// 8. Mobile & Store
// ════════════════════════════════════════════════════════════════

public record JobConfigMobileStoreDto
{
    // Mobile — admin-visible
    public required bool? BEnableTsicteams { get; init; }
    public required bool? BEnableMobileRsvp { get; init; }
    public required bool? BEnableMobileTeamChat { get; init; }
    public required bool BAllowMobileLogin { get; init; }
    public required bool? BAllowMobileRegn { get; init; }
    public required int? MobileScoreHoursPastGameEligible { get; init; }

    // SuperUser-only — Mobile
    public string? MobileJobName { get; init; }

    // SuperUser-only — Store
    public bool? BEnableStore { get; init; }
    public bool? BenableStp { get; init; }
    public string? StoreContactEmail { get; init; }
    public string? StoreRefundPolicy { get; init; }
    public string? StorePickupDetails { get; init; }
    public decimal? StoreSalesTax { get; init; }
    public decimal? StoreTsicrate { get; init; }
}

public record UpdateJobConfigMobileStoreRequest
{
    // Mobile — admin-visible
    public required bool? BEnableTsicteams { get; init; }
    public required bool? BEnableMobileRsvp { get; init; }
    public required bool? BEnableMobileTeamChat { get; init; }
    public required bool BAllowMobileLogin { get; init; }
    public required bool? BAllowMobileRegn { get; init; }
    public required int? MobileScoreHoursPastGameEligible { get; init; }

    // SuperUser-only (ignored for non-super callers)
    public string? MobileJobName { get; init; }
    public bool? BEnableStore { get; init; }
    public bool? BenableStp { get; init; }
    public string? StoreContactEmail { get; init; }
    public string? StoreRefundPolicy { get; init; }
    public string? StorePickupDetails { get; init; }
    public decimal? StoreSalesTax { get; init; }
    public decimal? StoreTsicrate { get; init; }
}

// ════════════════════════════════════════════════════════════════
// Reference Data (dropdowns)
// ════════════════════════════════════════════════════════════════

public record JobConfigReferenceDataDto
{
    public required List<JobTypeRefDto> JobTypes { get; init; }
    public required List<SportRefDto> Sports { get; init; }
    public required List<CustomerRefDto> Customers { get; init; }
    public required List<BillingTypeRefDto> BillingTypes { get; init; }
    public required List<ChargeTypeRefDto> ChargeTypes { get; init; }
}

public record JobTypeRefDto
{
    public required int JobTypeId { get; init; }
    public required string? Name { get; init; }
}

public record SportRefDto
{
    public required Guid SportId { get; init; }
    public required string? Name { get; init; }
}

public record CustomerRefDto
{
    public required Guid CustomerId { get; init; }
    public required string? Name { get; init; }
}

public record BillingTypeRefDto
{
    public required int BillingTypeId { get; init; }
    public required string? Name { get; init; }
}

public record ChargeTypeRefDto
{
    public required int Id { get; init; }
    public required string? Name { get; init; }
}
