using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AspNetUsers
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

    public virtual ICollection<AccountingPaymentMethods> AccountingPaymentMethods { get; set; } = new List<AccountingPaymentMethods>();

    public virtual ICollection<Agegroups> Agegroups { get; set; } = new List<Agegroups>();

    public virtual ICollection<ApiRosterPlayersAccessed> ApiRosterPlayersAccessed { get; set; } = new List<ApiRosterPlayersAccessed>();

    public virtual ICollection<AspNetUserClaims> AspNetUserClaims { get; set; } = new List<AspNetUserClaims>();

    public virtual ICollection<AspNetUserLogins> AspNetUserLogins { get; set; } = new List<AspNetUserLogins>();

    public virtual ICollection<BillingTypes> BillingTypes { get; set; } = new List<BillingTypes>();

    public virtual ICollection<BracketSeeds> BracketSeeds { get; set; } = new List<BracketSeeds>();

    public virtual ICollection<Bulletins> Bulletins { get; set; } = new List<Bulletins>();

    public virtual ICollection<CalendarEvents> CalendarEvents { get; set; } = new List<CalendarEvents>();

    public virtual ICollection<CellphonecarrierDomains> CellphonecarrierDomains { get; set; } = new List<CellphonecarrierDomains>();

    public virtual ICollection<ChatMessages> ChatMessages { get; set; } = new List<ChatMessages>();

    public virtual ICollection<Clubs> Clubs { get; set; } = new List<Clubs>();

    public virtual ICollection<ContactRelationshipCategories> ContactRelationshipCategories { get; set; } = new List<ContactRelationshipCategories>();

    public virtual ICollection<ContactTypeCategories> ContactTypeCategories { get; set; } = new List<ContactTypeCategories>();

    public virtual ICollection<Customers> Customers { get; set; } = new List<Customers>();

    public virtual ICollection<Divisions> Divisions { get; set; } = new List<Divisions>();

    public virtual ICollection<EmailFailures> EmailFailures { get; set; } = new List<EmailFailures>();

    public virtual ICollection<EmailLogs> EmailLogs { get; set; } = new List<EmailLogs>();

    public virtual Families? FamiliesFamilyUser { get; set; }

    public virtual ICollection<Families> FamiliesLebUser { get; set; } = new List<Families>();

    public virtual ICollection<FamilyMembers> FamilyMembers { get; set; } = new List<FamilyMembers>();

    public virtual ICollection<FieldsLeagueSeason> FieldsLeagueSeason { get; set; } = new List<FieldsLeagueSeason>();

    public virtual ICollection<Ftexts> Ftexts { get; set; } = new List<Ftexts>();

    public virtual ICollection<JobAgeRanges> JobAgeRanges { get; set; } = new List<JobAgeRanges>();

    public virtual ICollection<JobControllers> JobControllers { get; set; } = new List<JobControllers>();

    public virtual ICollection<JobCustomers> JobCustomers { get; set; } = new List<JobCustomers>();

    public virtual ICollection<JobDiscountCodes> JobDiscountCodes { get; set; } = new List<JobDiscountCodes>();

    public virtual ICollection<JobDisplayOptions> JobDisplayOptions { get; set; } = new List<JobDisplayOptions>();

    public virtual ICollection<JobLeagues> JobLeagues { get; set; } = new List<JobLeagues>();

    public virtual ICollection<JobMenuItems> JobMenuItems { get; set; } = new List<JobMenuItems>();

    public virtual ICollection<JobMenus> JobMenus { get; set; } = new List<JobMenus>();

    public virtual ICollection<JobOwlImages> JobOwlImages { get; set; } = new List<JobOwlImages>();

    public virtual ICollection<JobPushNotificationsToAll> JobPushNotificationsToAll { get; set; } = new List<JobPushNotificationsToAll>();

    public virtual ICollection<JobSmsbroadcasts> JobSmsbroadcasts { get; set; } = new List<JobSmsbroadcasts>();

    public virtual ICollection<Jobs> Jobs { get; set; } = new List<Jobs>();

    public virtual ICollection<Leagues> Leagues { get; set; } = new List<Leagues>();

    public virtual ICollection<MenuTypes> MenuTypes { get; set; } = new List<MenuTypes>();

    public virtual ICollection<MobileUserData> MobileUserData { get; set; } = new List<MobileUserData>();

    public virtual ICollection<MonthlyJobStats> MonthlyJobStats { get; set; } = new List<MonthlyJobStats>();

    public virtual ICollection<PairingsLeagueSeason> PairingsLeagueSeason { get; set; } = new List<PairingsLeagueSeason>();

    public virtual PersonContacts? PersonContacts { get; set; }

    public virtual ICollection<RefGameAssigments> RefGameAssigments { get; set; } = new List<RefGameAssigments>();

    public virtual ICollection<RegistrationAccounting> RegistrationAccounting { get; set; } = new List<RegistrationAccounting>();

    public virtual ICollection<Registrations> RegistrationsLebUser { get; set; } = new List<Registrations>();

    public virtual ICollection<Registrations> RegistrationsUser { get; set; } = new List<Registrations>();

    public virtual ICollection<Schedule> Schedule { get; set; } = new List<Schedule>();

    public virtual ICollection<Sports> Sports { get; set; } = new List<Sports>();

    public virtual ICollection<StoreCartBatchAccounting> StoreCartBatchAccounting { get; set; } = new List<StoreCartBatchAccounting>();

    public virtual ICollection<StoreCartBatchSkuEdits> StoreCartBatchSkuEdits { get; set; } = new List<StoreCartBatchSkuEdits>();

    public virtual ICollection<StoreCartBatchSkuQuantityAdjustments> StoreCartBatchSkuQuantityAdjustments { get; set; } = new List<StoreCartBatchSkuQuantityAdjustments>();

    public virtual ICollection<StoreCartBatchSkuRestocks> StoreCartBatchSkuRestocks { get; set; } = new List<StoreCartBatchSkuRestocks>();

    public virtual ICollection<StoreCartBatchSkus> StoreCartBatchSkus { get; set; } = new List<StoreCartBatchSkus>();

    public virtual ICollection<StoreCartBatches> StoreCartBatches { get; set; } = new List<StoreCartBatches>();

    public virtual ICollection<StoreCart> StoreCartFamilyUser { get; set; } = new List<StoreCart>();

    public virtual ICollection<StoreCart> StoreCartLebUser { get; set; } = new List<StoreCart>();

    public virtual ICollection<StoreColors> StoreColors { get; set; } = new List<StoreColors>();

    public virtual ICollection<StoreItemSkus> StoreItemSkus { get; set; } = new List<StoreItemSkus>();

    public virtual ICollection<StoreItems> StoreItems { get; set; } = new List<StoreItems>();

    public virtual ICollection<StoreSizes> StoreSizes { get; set; } = new List<StoreSizes>();

    public virtual ICollection<Stores> Stores { get; set; } = new List<Stores>();

    public virtual ICollection<TeamAttendanceEvents> TeamAttendanceEvents { get; set; } = new List<TeamAttendanceEvents>();

    public virtual ICollection<TeamAttendanceRecords> TeamAttendanceRecordsLebUser { get; set; } = new List<TeamAttendanceRecords>();

    public virtual ICollection<TeamAttendanceRecords> TeamAttendanceRecordsPlayer { get; set; } = new List<TeamAttendanceRecords>();

    public virtual ICollection<TeamDocs> TeamDocs { get; set; } = new List<TeamDocs>();

    public virtual ICollection<TeamEvents> TeamEvents { get; set; } = new List<TeamEvents>();

    public virtual ICollection<TeamGalleryPhotos> TeamGalleryPhotos { get; set; } = new List<TeamGalleryPhotos>();

    public virtual ICollection<TeamRosterRequests> TeamRosterRequests { get; set; } = new List<TeamRosterRequests>();

    public virtual ICollection<Teams> TeamsClubrep { get; set; } = new List<Teams>();

    public virtual ICollection<Teams> TeamsLebUser { get; set; } = new List<Teams>();

    public virtual ICollection<TimeslotsLeagueSeasonDates> TimeslotsLeagueSeasonDates { get; set; } = new List<TimeslotsLeagueSeasonDates>();

    public virtual ICollection<TimeslotsLeagueSeasonFields> TimeslotsLeagueSeasonFields { get; set; } = new List<TimeslotsLeagueSeasonFields>();

    public virtual ICollection<Timezones> Timezones { get; set; } = new List<Timezones>();

    public virtual ICollection<AspNetRoles> Role { get; set; } = new List<AspNetRoles>();
}
