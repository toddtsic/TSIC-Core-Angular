using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Jobs
{
    public int BillingTypeId { get; set; }

    public DateTime ExpiryAdmin { get; set; }

    public DateTime ExpiryUsers { get; set; }

    public int JobTypeId { get; set; }

    public byte[]? UpdatedOn { get; set; }

    public bool BAllowRosterViewAdult { get; set; }

    public bool BAllowRosterViewPlayer { get; set; }

    public bool BBannerIsCustom { get; set; }

    public bool BSuspendPublic { get; set; }

    public int PaymentMethodsAllowedCode { get; set; }

    public bool BAddProcessingFees { get; set; }

    public string? Balancedueaspercent { get; set; }

    public string? BannerFile { get; set; }

    public Guid CustomerId { get; set; }

    public int JobAi { get; set; }

    public string? JobDescription { get; set; }

    public Guid JobId { get; set; }

    public string? JobName { get; set; }

    public string JobPath { get; set; } = null!;

    public string? JobTagline { get; set; }

    public string? LebUserId { get; set; }

    public string? MailTo { get; set; }

    public string? MailinPaymentWarning { get; set; }

    public DateTime Modified { get; set; }

    public string? PayTo { get; set; }

    public decimal? PerMonthCharge { get; set; }

    public decimal? PerPlayerCharge { get; set; }

    public float? PerSalesPercentCharge { get; set; }

    public decimal? PerTeamCharge { get; set; }

    public string? SearchenginKeywords { get; set; }

    public string? SearchengineDescription { get; set; }

    public string? Season { get; set; }

    public Guid SportId { get; set; }

    public string? Year { get; set; }

    public string? PlayerRegConfirmationEmail { get; set; }

    public string? PlayerRegConfirmationOnScreen { get; set; }

    public string? PlayerRegRefundPolicy { get; set; }

    public string? PlayerRegReleaseOfLiability { get; set; }

    public string? PlayerRegCodeOfConduct { get; set; }

    public int? PlayerRegMultiPlayerDiscountMin { get; set; }

    public int? PlayerRegMultiPlayerDiscountPercent { get; set; }

    public string? AdultRegConfirmationEmail { get; set; }

    public string? AdultRegConfirmationOnScreen { get; set; }

    public string? AdultRegRefundPolicy { get; set; }

    public string? AdultRegReleaseOfLiability { get; set; }

    public string? AdultRegCodeOfConduct { get; set; }

    public string RegformNamePlayer { get; set; } = null!;

    public string RegformNameTeam { get; set; } = null!;

    public string RegformNameCoach { get; set; } = null!;

    public string RegformNameClubRep { get; set; } = null!;

    public string? RegFormBccs { get; set; }

    public string? RegFormCcs { get; set; }

    public string? RegFormFrom { get; set; }

    public string? JobNameQbp { get; set; }

    public string? DisplayName { get; set; }

    public bool? BTeamsFullPaymentRequired { get; set; }

    public bool? BClubRepAllowEdit { get; set; }

    public bool? BClubRepAllowDelete { get; set; }

    public bool? BClubRepAllowAdd { get; set; }

    public bool? BRestrictPlayerTeamsToAgerange { get; set; }

    public string? Rescheduleemaillist { get; set; }

    public string? Alwayscopyemaillist { get; set; }

    public bool BAllowMobileLogin { get; set; }

    public string? JsonOptions { get; set; }

    public string? CoreRegformPlayer { get; set; }

    public string? RefereeRegConfirmationEmail { get; set; }

    public string? RefereeRegConfirmationOnScreen { get; set; }

    public string? RecruiterRegConfirmationEmail { get; set; }

    public string? RecruiterRegConfirmationOnScreen { get; set; }

    public bool? BTeamPushDirectors { get; set; }

    public bool BShowTeamNameOnlyInSchedules { get; set; }

    public decimal? ProcessingFeePercent { get; set; }

    public DateTime? UslaxNumberValidThroughDate { get; set; }

    public string? MomLabel { get; set; }

    public string? DadLabel { get; set; }

    public bool BUseWaitlists { get; set; }

    public bool? BScheduleAllowPublicAccess { get; set; }

    public bool? BRegistrationAllowPlayer { get; set; }

    public bool? BRegistrationAllowTeam { get; set; }

    public bool? BAllowRefundsInPriorMonths { get; set; }

    public bool? BAllowCreditAll { get; set; }

    public string? PlayerRegCovid19Waiver { get; set; }

    public bool? AdnArb { get; set; }

    public int? AdnArbbillingOccurences { get; set; }

    public int? AdnArbintervalLength { get; set; }

    public DateTime? AdnArbstartDate { get; set; }

    public decimal? AdnArbMinimunTotalCharge { get; set; }

    public int? MobileScoreHoursPastGameEligible { get; set; }

    public bool? BSignalRschedule { get; set; }

    public bool? BDisallowCcplayerConfirmations { get; set; }

    public string? JobCode { get; set; }

    public bool? BAllowMobileRegn { get; set; }

    public bool? BApplyProcessingFeesToTeamDeposit { get; set; }

    public bool? BOfferPlayerRegsaverInsurance { get; set; }

    public bool? BEnableTsicteams { get; set; }

    public bool? BEnableMobileRsvp { get; set; }

    public bool? BEnableStore { get; set; }

    public string? MobileJobName { get; set; }

    public decimal StoreSalesTax { get; set; }

    public string? StoreRefundPolicy { get; set; }

    public string? StorePickupDetails { get; set; }

    public string? StoreContactEmail { get; set; }

    public DateTime? EventStartDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public bool? BOfferTeamRegsaverInsurance { get; set; }

    public bool? BEnableMobileTeamChat { get; set; }

    public string? PlayerProfileMetadataJson { get; set; }

    public bool? BenableStp { get; set; }

    public virtual BillingTypes BillingType { get; set; } = null!;

    public virtual ICollection<Bulletins> Bulletins { get; set; } = new List<Bulletins>();

    public virtual ICollection<CalendarEvents> CalendarEvents { get; set; } = new List<CalendarEvents>();

    public virtual Customers Customer { get; set; } = null!;

    public virtual ICollection<DeviceJobs> DeviceJobs { get; set; } = new List<DeviceJobs>();

    public virtual ICollection<EmailFailures> EmailFailures { get; set; } = new List<EmailFailures>();

    public virtual ICollection<EmailLogs> EmailLogs { get; set; } = new List<EmailLogs>();

    public virtual ICollection<GameClockParams> GameClockParams { get; set; } = new List<GameClockParams>();

    public virtual ICollection<JobAdminCharges> JobAdminCharges { get; set; } = new List<JobAdminCharges>();

    public virtual ICollection<JobAgeRanges> JobAgeRanges { get; set; } = new List<JobAgeRanges>();

    public virtual ICollection<JobCalendar> JobCalendar { get; set; } = new List<JobCalendar>();

    public virtual ICollection<JobCustomers> JobCustomers { get; set; } = new List<JobCustomers>();

    public virtual ICollection<JobDiscountCodes> JobDiscountCodes { get; set; } = new List<JobDiscountCodes>();

    public virtual JobDisplayOptions? JobDisplayOptions { get; set; }

    public virtual ICollection<JobLeagues> JobLeagues { get; set; } = new List<JobLeagues>();

    public virtual ICollection<JobMenus> JobMenus { get; set; } = new List<JobMenus>();

    public virtual ICollection<JobMessages> JobMessages { get; set; } = new List<JobMessages>();

    public virtual JobOwlImages? JobOwlImages { get; set; }

    public virtual ICollection<JobPushNotificationsToAll> JobPushNotificationsToAll { get; set; } = new List<JobPushNotificationsToAll>();

    public virtual ICollection<JobSmsbroadcasts> JobSmsbroadcasts { get; set; } = new List<JobSmsbroadcasts>();

    public virtual JobTypes JobType { get; set; } = null!;

    public virtual ICollection<Jobinvoices> Jobinvoices { get; set; } = new List<Jobinvoices>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<Menus> Menus { get; set; } = new List<Menus>();

    public virtual ICollection<MonthlyJobStats> MonthlyJobStats { get; set; } = new List<MonthlyJobStats>();

    public virtual ICollection<PushNotifications> PushNotifications { get; set; } = new List<PushNotifications>();

    public virtual ICollection<PushSubscriptionJobs> PushSubscriptionJobs { get; set; } = new List<PushSubscriptionJobs>();

    public virtual ICollection<RegForms> RegForms { get; set; } = new List<RegForms>();

    public virtual ICollection<Registrations> Registrations { get; set; } = new List<Registrations>();

    public virtual ICollection<Schedule> Schedule { get; set; } = new List<Schedule>();

    public virtual ICollection<Sliders> Sliders { get; set; } = new List<Sliders>();

    public virtual Sports Sport { get; set; } = null!;

    public virtual ICollection<Stores> Stores { get; set; } = new List<Stores>();

    public virtual ICollection<TeamDocs> TeamDocs { get; set; } = new List<TeamDocs>();

    public virtual ICollection<TeamEvents> TeamEvents { get; set; } = new List<TeamEvents>();

    public virtual ICollection<Teams> Teams { get; set; } = new List<Teams>();
}
