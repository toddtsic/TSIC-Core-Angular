using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Registration
{
    public int RegistrationAi { get; set; }

    public string? RegistrationCategory { get; set; }

    public string? RegistrationGroupId { get; set; }

    public Guid RegistrationId { get; set; }

    public DateTime RegistrationTs { get; set; }

    public string? RoleId { get; set; }

    public string? UserId { get; set; }

    public string? FamilyUserId { get; set; }

    public bool? BActive { get; set; }

    public bool BConfirmationSent { get; set; }

    public Guid JobId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? RegistrationFormName { get; set; }

    public Guid? AssignedTeamId { get; set; }

    public int? PaymentMethodChosen { get; set; }

    public decimal FeeProcessing { get; set; }

    public decimal FeeBase { get; set; }

    public decimal FeeDiscount { get; set; }

    public decimal FeeDiscountMp { get; set; }

    public decimal FeeDonation { get; set; }

    public decimal FeeLatefee { get; set; }

    public decimal FeeTotal { get; set; }

    public decimal OwedTotal { get; set; }

    public decimal PaidTotal { get; set; }

    public string? CampsAttending { get; set; }

    public Guid? CustomerId { get; set; }

    public string? Honors { get; set; }

    public string? LeaguesAttending { get; set; }

    public string? PreviousCoach1 { get; set; }

    public string? PreviousCoach2 { get; set; }

    public string? Act { get; set; }

    public bool BBgcheck { get; set; }

    public bool BMedAlert { get; set; }

    public bool BScholarshipRequested { get; set; }

    public bool BTravel { get; set; }

    public bool BWaiverSigned1 { get; set; }

    public bool BWaiverSigned2 { get; set; }

    public bool BWaiverSigned3 { get; set; }

    public string? BackcheckExplain { get; set; }

    public DateTime? BgCheckDate { get; set; }

    public DateTime? CertDate { get; set; }

    public string? CertNo { get; set; }

    public string? ClassRank { get; set; }

    public string? ClubName { get; set; }

    public string? Gpa { get; set; }

    public string? GradYear { get; set; }

    public string? HealthInsurer { get; set; }

    public string? HealthInsurerGroupNo { get; set; }

    public string? HealthInsurerPhone { get; set; }

    public string? HealthInsurerPolicyNo { get; set; }

    public string? HeightInches { get; set; }

    public string? InsuredName { get; set; }

    public string? JerseySize { get; set; }

    public string? Kilt { get; set; }

    public string? MedicalNote { get; set; }

    public string? Position { get; set; }

    public string? SchoolTeamName { get; set; }

    public string? Psat { get; set; }

    public string? Region { get; set; }

    public Guid? RequestedAgegroupId { get; set; }

    public string? RoommatePref { get; set; }

    public string? Sat { get; set; }

    public string? SatMath { get; set; }

    public string? SatVerbal { get; set; }

    public string? SatWriting { get; set; }

    public string? SchoolGrade { get; set; }

    public string? SchoolName { get; set; }

    public string? Shoes { get; set; }

    public string? ShortsSize { get; set; }

    public string? SportAssnId { get; set; }

    public DateTime? SportAssnIdexpDate { get; set; }

    public string? SportYearsExp { get; set; }

    public string? TShirt { get; set; }

    public string? UniformNo { get; set; }

    public string? VolChildreninprogram { get; set; }

    public string? Volposition { get; set; }

    public string? WeightLbs { get; set; }

    public string? NightGroup { get; set; }

    public string? DayGroup { get; set; }

    public Guid? AssignedAgegroupId { get; set; }

    public Guid? AssignedCustomerId { get; set; }

    public Guid? AssignedDivId { get; set; }

    public Guid? AssignedLeagueId { get; set; }

    public string? Assignment { get; set; }

    public string? SpecialRequests { get; set; }

    public string? Reversible { get; set; }

    public string? Gloves { get; set; }

    public string? Sweatshirt { get; set; }

    public int? DiscountCodeId { get; set; }

    public double? FiveTenFive { get; set; }

    public double? Threehundredshuttle { get; set; }

    public double? Fourtyyarddash { get; set; }

    public double? Fastestshot { get; set; }

    public bool? BCollegeCommit { get; set; }

    public string? CollegeCommit { get; set; }

    public string? WhoReferred { get; set; }

    public string? StrongHand { get; set; }

    public string? ClubCoach { get; set; }

    public string? ClubCoachEmail { get; set; }

    public string? SchoolCoach { get; set; }

    public string? SchoolCoachEmail { get; set; }

    public string? SchoolActivities { get; set; }

    public string? HonorsAcademic { get; set; }

    public string? HonorsAthletic { get; set; }

    public string? OtherSports { get; set; }

    public string? HeadshotPath { get; set; }

    public string? SchoolLevelClasses { get; set; }

    public string? Height { get; set; }

    public string? MomTwitter { get; set; }

    public string? DadTwitter { get; set; }

    public string? MomInstagram { get; set; }

    public string? DadInstagram { get; set; }

    public string? Twitter { get; set; }

    public string? Instagram { get; set; }

    public string? ClubTeamName { get; set; }

    public string? Sweatpants { get; set; }

    public string? SkillLevel { get; set; }

    public bool? BWaiverSignedCv19 { get; set; }

    public string? AdnSubscriptionId { get; set; }

    public string? AdnSubscriptionStatus { get; set; }

    public DateTime? AdnSubscriptionStartDate { get; set; }

    public int? AdnSubscriptionBillingOccurences { get; set; }

    public decimal? AdnSubscriptionAmountPerOccurence { get; set; }

    public int? AdnSubscriptionIntervalLength { get; set; }

    public Guid? RegformId { get; set; }

    public string? Snapchat { get; set; }

    public bool? BUploadedInsuranceCard { get; set; }

    public bool? BUploadedVaccineCard { get; set; }

    public bool? BUploadedMedForm { get; set; }

    public DateTime? ModifiedMobile { get; set; }

    public string? RegsaverPolicyId { get; set; }

    public string? TikTokHandle { get; set; }

    public string? RecruitingHandle { get; set; }

    public DateTime? RegsaverPolicyIdCreateDate { get; set; }

    public virtual AccountingApplyToSummary? AccountingApplyToSummary { get; set; }

    public virtual ICollection<ApiRosterPlayersAccessed> ApiRosterPlayersAccesseds { get; set; } = new List<ApiRosterPlayersAccessed>();

    public virtual Team? AssignedTeam { get; set; }

    public virtual ICollection<DeviceRegistrationId> DeviceRegistrationIds { get; set; } = new List<DeviceRegistrationId>();

    public virtual ICollection<DeviceTeam> DeviceTeams { get; set; } = new List<DeviceTeam>();

    public virtual JobDiscountCode? DiscountCode { get; set; }

    public virtual Family? FamilyUser { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual ICollection<JobMessage> JobMessages { get; set; } = new List<JobMessage>();

    public virtual ICollection<JobReportExportHistory> JobReportExportHistories { get; set; } = new List<JobReportExportHistory>();

    public virtual AspNetUser? LebUser { get; set; }

    public virtual ICollection<PushNotification> PushNotificationAuthorRegistrations { get; set; } = new List<PushNotification>();

    public virtual ICollection<PushNotification> PushNotificationQpRegs { get; set; } = new List<PushNotification>();

    public virtual ICollection<PushSubscriptionRegistration> PushSubscriptionRegistrations { get; set; } = new List<PushSubscriptionRegistration>();

    public virtual ICollection<RefGameAssigment> RefGameAssigments { get; set; } = new List<RefGameAssigment>();

    public virtual RegForm? Regform { get; set; }

    public virtual ICollection<RegistrationAccounting> RegistrationAccountings { get; set; } = new List<RegistrationAccounting>();

    public virtual AspNetRole? Role { get; set; }

    public virtual ICollection<StoreCartBatchSku> StoreCartBatchSkus { get; set; } = new List<StoreCartBatchSku>();

    public virtual ICollection<Team> TeamClubrepRegistrations { get; set; } = new List<Team>();

    public virtual ICollection<TeamMessage> TeamMessages { get; set; } = new List<TeamMessage>();

    public virtual ICollection<TeamSignupEvent> TeamSignupEvents { get; set; } = new List<TeamSignupEvent>();

    public virtual ICollection<TeamSignupEventsRegistration> TeamSignupEventsRegistrations { get; set; } = new List<TeamSignupEventsRegistration>();

    public virtual ICollection<Team> TeamViPolicyClubRepRegs { get; set; } = new List<Team>();

    public virtual AspNetUser? User { get; set; }
}
