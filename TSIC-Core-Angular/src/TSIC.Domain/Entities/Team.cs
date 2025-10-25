using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Team
{
    public bool? Active { get; set; }

    public Guid AgegroupId { get; set; }

    public string? AgegroupRequested { get; set; }

    public bool BHideRoster { get; set; }

    public DateTime Createdate { get; set; }

    public Guid? CustomerId { get; set; }

    public string? District { get; set; }

    public Guid? DivId { get; set; }

    public int DivRank { get; set; }

    public string? DivisionRequested { get; set; }

    public string? Dow { get; set; }

    public string? Dow2 { get; set; }

    public DateTime? Effectiveasofdate { get; set; }

    public DateTime? Enddate { get; set; }

    public DateTime? Expireondate { get; set; }

    public Guid? FieldId1 { get; set; }

    public Guid? FieldId2 { get; set; }

    public Guid? FieldId3 { get; set; }

    public string? Gender { get; set; }

    public Guid JobId { get; set; }

    public string? LastLeagueRecord { get; set; }

    public Guid LeagueId { get; set; }

    public string? LebUserId { get; set; }

    public string? LevelOfPlay { get; set; }

    public int MaxCount { get; set; }

    public DateTime Modified { get; set; }

    public decimal? PerRegistrantDeposit { get; set; }

    public decimal? PerRegistrantFee { get; set; }

    public Guid? PrevTeamId { get; set; }

    public string? Requests { get; set; }

    public string? Season { get; set; }

    public DateTime? Startdate { get; set; }

    public Guid TeamId { get; set; }

    public string? TeamName { get; set; }

    public string? TeamComments { get; set; }

    public int? TeamNumber { get; set; }

    public string? Year { get; set; }

    public DateTime? LateFeeEnd { get; set; }

    public short? SchoolGradeMin { get; set; }

    public short? SchoolGradeMax { get; set; }

    public int? GradYearMin { get; set; }

    public int? GradYearMax { get; set; }

    public decimal? LateFee { get; set; }

    public DateTime? LateFeeStart { get; set; }

    public bool? BAllowSelfRostering { get; set; }

    public DateOnly? DobMin { get; set; }

    public DateOnly? DobMax { get; set; }

    public decimal? DiscountFee { get; set; }

    public DateTime? DiscountFeeStart { get; set; }

    public DateTime? DiscountFeeEnd { get; set; }

    public string? ClubrepId { get; set; }

    public Guid? ClubrepRegistrationid { get; set; }

    public string? Color { get; set; }

    public decimal? FeeBase { get; set; }

    public decimal? FeeDiscount { get; set; }

    public decimal? FeeDiscountMp { get; set; }

    public decimal? FeeDonation { get; set; }

    public decimal? FeeLatefee { get; set; }

    public decimal? FeeTotal { get; set; }

    public decimal? OwedTotal { get; set; }

    public decimal? PaidTotal { get; set; }

    public decimal? FeeProcessing { get; set; }

    public int TeamAi { get; set; }

    public string? KeywordPairs { get; set; }

    public int? Games { get; set; }

    public int? Wins { get; set; }

    public int? Losses { get; set; }

    public int? Ties { get; set; }

    public int? Points { get; set; }

    public int? GoalsFor { get; set; }

    public int? GoalsVs { get; set; }

    public string? DisplayName { get; set; }

    public int? TbKey { get; set; }

    public string? TeamFullName { get; set; }

    public Guid? LeagueTeamId { get; set; }

    public int? StandingsRank { get; set; }

    public bool? BnewCoach { get; set; }

    public bool? BnewTeam { get; set; }

    public string? OldCoach { get; set; }

    public int? NoReturningPlayers { get; set; }

    public string? LastSeasonYear { get; set; }

    public string? OldTeamName { get; set; }

    public string? AdnSubscriptionId { get; set; }

    public string? AdnSubscriptionStatus { get; set; }

    public DateTime? AdnSubscriptionStartDate { get; set; }

    public int? AdnSubscriptionBillingOccurences { get; set; }

    public decimal? AdnSubscriptionAmountPerOccurence { get; set; }

    public int? AdnSubscriptionIntervalLength { get; set; }

    public bool? BDoNotValidateUslaxNumber { get; set; }

    public string? ViPolicyId { get; set; }

    public Guid? ViPolicyClubRepRegId { get; set; }

    public DateTime? ViPolicyCreateDate { get; set; }

    public int? GoalDiff9 { get; set; }

    public virtual Agegroup Agegroup { get; set; } = null!;

    public virtual ICollection<CalendarEvent> CalendarEvents { get; set; } = new List<CalendarEvent>();

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual AspNetUser? Clubrep { get; set; }

    public virtual Registration? ClubrepRegistration { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<DeviceTeam> DeviceTeams { get; set; } = new List<DeviceTeam>();

    public virtual Division? Div { get; set; }

    public virtual Field? FieldId1Navigation { get; set; }

    public virtual Field? FieldId2Navigation { get; set; }

    public virtual Field? FieldId3Navigation { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual ICollection<JobPushNotificationsToAll> JobPushNotificationsToAlls { get; set; } = new List<JobPushNotificationsToAll>();

    public virtual League League { get; set; } = null!;

    public virtual AspNetUser? LebUser { get; set; }

    public virtual ICollection<PushNotification> PushNotifications { get; set; } = new List<PushNotification>();

    public virtual ICollection<PushSubscriptionTeam> PushSubscriptionTeams { get; set; } = new List<PushSubscriptionTeam>();

    public virtual ICollection<RegistrationAccounting> RegistrationAccountings { get; set; } = new List<RegistrationAccounting>();

    public virtual ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    public virtual ICollection<Schedule> ScheduleT1s { get; set; } = new List<Schedule>();

    public virtual ICollection<Schedule> ScheduleT2s { get; set; } = new List<Schedule>();

    public virtual ICollection<TeamAttendanceEvent> TeamAttendanceEvents { get; set; } = new List<TeamAttendanceEvent>();

    public virtual ICollection<TeamDoc> TeamDocs { get; set; } = new List<TeamDoc>();

    public virtual ICollection<TeamEvent> TeamEvents { get; set; } = new List<TeamEvent>();

    public virtual ICollection<TeamGalleryPhoto> TeamGalleryPhotos { get; set; } = new List<TeamGalleryPhoto>();

    public virtual ICollection<TeamRosterRequest> TeamRosterRequests { get; set; } = new List<TeamRosterRequest>();

    public virtual ICollection<TeamSignupEvent> TeamSignupEvents { get; set; } = new List<TeamSignupEvent>();

    public virtual ICollection<TeamTournamentMapping> TeamTournamentMappingClubTeams { get; set; } = new List<TeamTournamentMapping>();

    public virtual ICollection<TeamTournamentMapping> TeamTournamentMappingTournamentTeams { get; set; } = new List<TeamTournamentMapping>();

    public virtual Registration? ViPolicyClubRepReg { get; set; }
}
