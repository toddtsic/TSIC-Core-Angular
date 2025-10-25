using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AspNetUser
{
    public int AccessFailedCount { get; set; }

    public string? ConcurrencyStamp { get; set; }

    public string? Email { get; set; }

    public bool EmailConfirmed { get; set; }

    public string? FirstName { get; set; }

    public string Id { get; set; } = null!;

    public string? LastName { get; set; }

    public bool LockoutEnabled { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public DateTime? LockoutEndDateUtc { get; set; }

    public string? NormalizedEmail { get; set; }

    public string? NormalizedUserName { get; set; }

    public string? PasswordHash { get; set; }

    public string? PhoneNumber { get; set; }

    public bool PhoneNumberConfirmed { get; set; }

    public string? SecurityStamp { get; set; }

    public bool TwoFactorEnabled { get; set; }

    public string? UserName { get; set; }

    public string? Cellphone { get; set; }

    public string? CellphoneProvider { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public DateTime? Dob { get; set; }

    public string? Fax { get; set; }

    public string? Gender { get; set; }

    public byte[]? ImageFile { get; set; }

    public string? ImageFileMimeType { get; set; }

    public string? Phone { get; set; }

    public string? PostalCode { get; set; }

    public string? State { get; set; }

    public string? StreetAddress { get; set; }

    public string? Workphone { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public bool BTsicwaiverSigned { get; set; }

    public DateTime? TsicwaiverSignedTs { get; set; }

    public DateTime? CreateDate { get; set; }

    public bool StsHasUnsubscribed { get; set; }

    public virtual ICollection<AccountingPaymentMethod> AccountingPaymentMethods { get; set; } = new List<AccountingPaymentMethod>();

    public virtual ICollection<Agegroup> Agegroups { get; set; } = new List<Agegroup>();

    public virtual ICollection<ApiRosterPlayersAccessed> ApiRosterPlayersAccesseds { get; set; } = new List<ApiRosterPlayersAccessed>();

    public virtual ICollection<AspNetUserClaim> AspNetUserClaims { get; set; } = new List<AspNetUserClaim>();

    public virtual ICollection<AspNetUserLogin> AspNetUserLogins { get; set; } = new List<AspNetUserLogin>();

    public virtual ICollection<BillingType> BillingTypes { get; set; } = new List<BillingType>();

    public virtual ICollection<BracketSeed> BracketSeeds { get; set; } = new List<BracketSeed>();

    public virtual ICollection<Bulletin> Bulletins { get; set; } = new List<Bulletin>();

    public virtual ICollection<CalendarEvent> CalendarEvents { get; set; } = new List<CalendarEvent>();

    public virtual ICollection<CellphonecarrierDomain> CellphonecarrierDomains { get; set; } = new List<CellphonecarrierDomain>();

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual ICollection<Club> Clubs { get; set; } = new List<Club>();

    public virtual ICollection<ContactRelationshipCategory> ContactRelationshipCategories { get; set; } = new List<ContactRelationshipCategory>();

    public virtual ICollection<ContactTypeCategory> ContactTypeCategories { get; set; } = new List<ContactTypeCategory>();

    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();

    public virtual ICollection<Division> Divisions { get; set; } = new List<Division>();

    public virtual ICollection<EmailFailure> EmailFailures { get; set; } = new List<EmailFailure>();

    public virtual ICollection<EmailLog> EmailLogs { get; set; } = new List<EmailLog>();

    public virtual Family? FamilyFamilyUser { get; set; }

    public virtual ICollection<Family> FamilyLebUsers { get; set; } = new List<Family>();

    public virtual ICollection<FamilyMember> FamilyMembers { get; set; } = new List<FamilyMember>();

    public virtual ICollection<FieldsLeagueSeason> FieldsLeagueSeasons { get; set; } = new List<FieldsLeagueSeason>();

    public virtual ICollection<Ftext> Ftexts { get; set; } = new List<Ftext>();

    public virtual ICollection<JobAgeRange> JobAgeRanges { get; set; } = new List<JobAgeRange>();

    public virtual ICollection<JobController> JobControllers { get; set; } = new List<JobController>();

    public virtual ICollection<JobCustomer> JobCustomers { get; set; } = new List<JobCustomer>();

    public virtual ICollection<JobDiscountCode> JobDiscountCodes { get; set; } = new List<JobDiscountCode>();

    public virtual ICollection<JobDisplayOption> JobDisplayOptions { get; set; } = new List<JobDisplayOption>();

    public virtual ICollection<JobLeague> JobLeagues { get; set; } = new List<JobLeague>();

    public virtual ICollection<JobMenuItem> JobMenuItems { get; set; } = new List<JobMenuItem>();

    public virtual ICollection<JobMenu> JobMenus { get; set; } = new List<JobMenu>();

    public virtual ICollection<JobOwlImage> JobOwlImages { get; set; } = new List<JobOwlImage>();

    public virtual ICollection<JobPushNotificationsToAll> JobPushNotificationsToAlls { get; set; } = new List<JobPushNotificationsToAll>();

    public virtual ICollection<JobSmsbroadcast> JobSmsbroadcasts { get; set; } = new List<JobSmsbroadcast>();

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    public virtual ICollection<League> Leagues { get; set; } = new List<League>();

    public virtual ICollection<MenuType> MenuTypes { get; set; } = new List<MenuType>();

    public virtual ICollection<MobileUserDatum> MobileUserData { get; set; } = new List<MobileUserDatum>();

    public virtual ICollection<MonthlyJobStat> MonthlyJobStats { get; set; } = new List<MonthlyJobStat>();

    public virtual ICollection<PairingsLeagueSeason> PairingsLeagueSeasons { get; set; } = new List<PairingsLeagueSeason>();

    public virtual PersonContact? PersonContact { get; set; }

    public virtual ICollection<RefGameAssigment> RefGameAssigments { get; set; } = new List<RefGameAssigment>();

    public virtual ICollection<RegistrationAccounting> RegistrationAccountings { get; set; } = new List<RegistrationAccounting>();

    public virtual ICollection<Registration> RegistrationLebUsers { get; set; } = new List<Registration>();

    public virtual ICollection<Registration> RegistrationUsers { get; set; } = new List<Registration>();

    public virtual ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();

    public virtual ICollection<Sport> Sports { get; set; } = new List<Sport>();

    public virtual ICollection<StoreCartBatchAccounting> StoreCartBatchAccountings { get; set; } = new List<StoreCartBatchAccounting>();

    public virtual ICollection<StoreCartBatchSkuEdit> StoreCartBatchSkuEdits { get; set; } = new List<StoreCartBatchSkuEdit>();

    public virtual ICollection<StoreCartBatchSkuQuantityAdjustment> StoreCartBatchSkuQuantityAdjustments { get; set; } = new List<StoreCartBatchSkuQuantityAdjustment>();

    public virtual ICollection<StoreCartBatchSkuRestock> StoreCartBatchSkuRestocks { get; set; } = new List<StoreCartBatchSkuRestock>();

    public virtual ICollection<StoreCartBatchSku> StoreCartBatchSkus { get; set; } = new List<StoreCartBatchSku>();

    public virtual ICollection<StoreCartBatch> StoreCartBatches { get; set; } = new List<StoreCartBatch>();

    public virtual ICollection<StoreCart> StoreCartFamilyUsers { get; set; } = new List<StoreCart>();

    public virtual ICollection<StoreCart> StoreCartLebUsers { get; set; } = new List<StoreCart>();

    public virtual ICollection<StoreColor> StoreColors { get; set; } = new List<StoreColor>();

    public virtual ICollection<StoreItemSku> StoreItemSkus { get; set; } = new List<StoreItemSku>();

    public virtual ICollection<StoreItem> StoreItems { get; set; } = new List<StoreItem>();

    public virtual ICollection<StoreSize> StoreSizes { get; set; } = new List<StoreSize>();

    public virtual ICollection<Store> Stores { get; set; } = new List<Store>();

    public virtual ICollection<TeamAttendanceEvent> TeamAttendanceEvents { get; set; } = new List<TeamAttendanceEvent>();

    public virtual ICollection<TeamAttendanceRecord> TeamAttendanceRecordLebUsers { get; set; } = new List<TeamAttendanceRecord>();

    public virtual ICollection<TeamAttendanceRecord> TeamAttendanceRecordPlayers { get; set; } = new List<TeamAttendanceRecord>();

    public virtual ICollection<Team> TeamClubreps { get; set; } = new List<Team>();

    public virtual ICollection<TeamDoc> TeamDocs { get; set; } = new List<TeamDoc>();

    public virtual ICollection<TeamEvent> TeamEvents { get; set; } = new List<TeamEvent>();

    public virtual ICollection<TeamGalleryPhoto> TeamGalleryPhotos { get; set; } = new List<TeamGalleryPhoto>();

    public virtual ICollection<Team> TeamLebUsers { get; set; } = new List<Team>();

    public virtual ICollection<TeamRosterRequest> TeamRosterRequests { get; set; } = new List<TeamRosterRequest>();

    public virtual ICollection<TimeslotsLeagueSeasonDate> TimeslotsLeagueSeasonDates { get; set; } = new List<TimeslotsLeagueSeasonDate>();

    public virtual ICollection<TimeslotsLeagueSeasonField> TimeslotsLeagueSeasonFields { get; set; } = new List<TimeslotsLeagueSeasonField>();

    public virtual ICollection<Timezone> Timezones { get; set; } = new List<Timezone>();

    public virtual ICollection<AspNetRole> Roles { get; set; } = new List<AspNetRole>();
}
