using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TSIC.Domain.Entities;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class SqlDbContext : DbContext
{
    public SqlDbContext(DbContextOptions<SqlDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AccountingApplyToSummaries> AccountingApplyToSummaries { get; set; }

    public virtual DbSet<AccountingPaymentMethods> AccountingPaymentMethods { get; set; }

    public virtual DbSet<Adn0714And15Records> Adn0714And15Records { get; set; }

    public virtual DbSet<AdndataFromPhoenix> AdndataFromPhoenix { get; set; }

    public virtual DbSet<AdntripleThreatData> AdntripleThreatData { get; set; }

    public virtual DbSet<Agegroups> Agegroups { get; set; }

    public virtual DbSet<ApiClaims> ApiClaims { get; set; }

    public virtual DbSet<ApiProperties> ApiProperties { get; set; }

    public virtual DbSet<ApiResources> ApiResources { get; set; }

    public virtual DbSet<ApiRosterPlayersAccessed> ApiRosterPlayersAccessed { get; set; }

    public virtual DbSet<ApiScopeClaims> ApiScopeClaims { get; set; }

    public virtual DbSet<ApiScopes> ApiScopes { get; set; }

    public virtual DbSet<ApiSecrets> ApiSecrets { get; set; }

    public virtual DbSet<AspNetRoleClaims> AspNetRoleClaims { get; set; }

    public virtual DbSet<AspNetRoles> AspNetRoles { get; set; }

    public virtual DbSet<AspNetUserClaims> AspNetUserClaims { get; set; }

    public virtual DbSet<AspNetUserLogins> AspNetUserLogins { get; set; }

    public virtual DbSet<AspNetUsers> AspNetUsers { get; set; }

    public virtual DbSet<BillingTypes> BillingTypes { get; set; }

    public virtual DbSet<BracketDataSingleElimination> BracketDataSingleElimination { get; set; }

    public virtual DbSet<BracketSeeds> BracketSeeds { get; set; }

    public virtual DbSet<Bulletins> Bulletins { get; set; }

    public virtual DbSet<CalendarEvents> CalendarEvents { get; set; }

    public virtual DbSet<CellphonecarrierDomains> CellphonecarrierDomains { get; set; }

    public virtual DbSet<Charlieamericanselectconsolations2021> Charlieamericanselectconsolations2021 { get; set; }

    public virtual DbSet<ChatMessages> ChatMessages { get; set; }

    public virtual DbSet<ClientClaims> ClientClaims { get; set; }

    public virtual DbSet<ClientCorsOrigins> ClientCorsOrigins { get; set; }

    public virtual DbSet<ClientGrantTypes> ClientGrantTypes { get; set; }

    public virtual DbSet<ClientIdPrestrictions> ClientIdPrestrictions { get; set; }

    public virtual DbSet<ClientPostLogoutRedirectUris> ClientPostLogoutRedirectUris { get; set; }

    public virtual DbSet<ClientProperties> ClientProperties { get; set; }

    public virtual DbSet<ClientRedirectUris> ClientRedirectUris { get; set; }

    public virtual DbSet<ClientScopes> ClientScopes { get; set; }

    public virtual DbSet<ClientSecrets> ClientSecrets { get; set; }

    public virtual DbSet<Clients> Clients { get; set; }

    public virtual DbSet<ClubReps> ClubReps { get; set; }

    public virtual DbSet<ClubTeams> ClubTeams { get; set; }

    public virtual DbSet<Clubs> Clubs { get; set; }

    public virtual DbSet<Clubs1> Clubs1 { get; set; }

    public virtual DbSet<ContactRelationshipCategories> ContactRelationshipCategories { get; set; }

    public virtual DbSet<ContactTypeCategories> ContactTypeCategories { get; set; }

    public virtual DbSet<CustomerGroupCustomers> CustomerGroupCustomers { get; set; }

    public virtual DbSet<CustomerGroups> CustomerGroups { get; set; }

    public virtual DbSet<Customers> Customers { get; set; }

    public virtual DbSet<DeviceCodes> DeviceCodes { get; set; }

    public virtual DbSet<DeviceGids> DeviceGids { get; set; }

    public virtual DbSet<DeviceJobs> DeviceJobs { get; set; }

    public virtual DbSet<DeviceRegistrationIds> DeviceRegistrationIds { get; set; }

    public virtual DbSet<DeviceTeams> DeviceTeams { get; set; }

    public virtual DbSet<Devices> Devices { get; set; }

    public virtual DbSet<Divisions> Divisions { get; set; }

    public virtual DbSet<EmailFailures> EmailFailures { get; set; }

    public virtual DbSet<EmailLast100> EmailLast100 { get; set; }

    public virtual DbSet<EmailLogs> EmailLogs { get; set; }

    public virtual DbSet<Families> Families { get; set; }

    public virtual DbSet<FamilyMembers> FamilyMembers { get; set; }

    public virtual DbSet<FieldOverridesStartTimeMaxMinGames> FieldOverridesStartTimeMaxMinGames { get; set; }

    public virtual DbSet<Fields> Fields { get; set; }

    public virtual DbSet<FieldsLeagueSeason> FieldsLeagueSeason { get; set; }

    public virtual DbSet<Ftexts> Ftexts { get; set; }

    public virtual DbSet<GameClockParams> GameClockParams { get; set; }

    public virtual DbSet<GameStatusCodes> GameStatusCodes { get; set; }

    public virtual DbSet<IdentityClaims> IdentityClaims { get; set; }

    public virtual DbSet<IdentityProperties> IdentityProperties { get; set; }

    public virtual DbSet<IdentityResources> IdentityResources { get; set; }

    public virtual DbSet<IwlcaCapitalCup> IwlcaCapitalCup { get; set; }

    public virtual DbSet<IwlcaChampionsCup> IwlcaChampionsCup { get; set; }

    public virtual DbSet<IwlcaDebut> IwlcaDebut { get; set; }

    public virtual DbSet<IwlcaNewEngland> IwlcaNewEngland { get; set; }

    public virtual DbSet<IwlcaPresidentsCup> IwlcaPresidentsCup { get; set; }

    public virtual DbSet<IwlcaSoutheastCup> IwlcaSoutheastCup { get; set; }

    public virtual DbSet<IwlcaSouthwestCup> IwlcaSouthwestCup { get; set; }

    public virtual DbSet<IwlcaWestCoastCup> IwlcaWestCoastCup { get; set; }

    public virtual DbSet<JobAdminChargeTypes> JobAdminChargeTypes { get; set; }

    public virtual DbSet<JobAdminCharges> JobAdminCharges { get; set; }

    public virtual DbSet<JobAgeRanges> JobAgeRanges { get; set; }

    public virtual DbSet<JobCalendar> JobCalendar { get; set; }

    public virtual DbSet<JobControllers> JobControllers { get; set; }

    public virtual DbSet<JobCustomers> JobCustomers { get; set; }

    public virtual DbSet<JobDirectorsRegistrationTs> JobDirectorsRegistrationTs { get; set; }

    public virtual DbSet<JobDiscountCodes> JobDiscountCodes { get; set; }

    public virtual DbSet<JobDisplayOptions> JobDisplayOptions { get; set; }

    public virtual DbSet<JobInvoiceNumbers> JobInvoiceNumbers { get; set; }

    public virtual DbSet<JobLeagues> JobLeagues { get; set; }

    public virtual DbSet<JobMenuItems> JobMenuItems { get; set; }

    public virtual DbSet<JobMenus> JobMenus { get; set; }

    public virtual DbSet<JobMessages> JobMessages { get; set; }

    public virtual DbSet<JobOwlImages> JobOwlImages { get; set; }

    public virtual DbSet<JobPushNotificationsToAll> JobPushNotificationsToAll { get; set; }

    public virtual DbSet<JobReportExportHistory> JobReportExportHistory { get; set; }

    public virtual DbSet<JobSmsbroadcasts> JobSmsbroadcasts { get; set; }

    public virtual DbSet<JobTypes> JobTypes { get; set; }

    public virtual DbSet<JobWidget> JobWidget { get; set; }

    public virtual DbSet<Jobinvoices> Jobinvoices { get; set; }

    public virtual DbSet<Jobs> Jobs { get; set; }

    public virtual DbSet<JobsToPurgeRemainingJobIds> JobsToPurgeRemainingJobIds { get; set; }

    public virtual DbSet<LeagueAgeGroupGameDayInfo> LeagueAgeGroupGameDayInfo { get; set; }

    public virtual DbSet<Leagues> Leagues { get; set; }

    public virtual DbSet<Masterpairingtable> Masterpairingtable { get; set; }

    public virtual DbSet<MenuItems> MenuItems { get; set; }

    public virtual DbSet<MenuTypes> MenuTypes { get; set; }

    public virtual DbSet<Menus> Menus { get; set; }

    public virtual DbSet<MigrationHistoryOld> MigrationHistoryOld { get; set; }

    public virtual DbSet<MobileUserData> MobileUserData { get; set; }

    public virtual DbSet<MonthlyJobStats> MonthlyJobStats { get; set; }

    public virtual DbSet<NuveiBatches> NuveiBatches { get; set; }

    public virtual DbSet<NuveiFunding> NuveiFunding { get; set; }

    public virtual DbSet<OpenIddictApplications> OpenIddictApplications { get; set; }

    public virtual DbSet<OpenIddictAuthorizations> OpenIddictAuthorizations { get; set; }

    public virtual DbSet<OpenIddictScopes> OpenIddictScopes { get; set; }

    public virtual DbSet<OpenIddictTokens> OpenIddictTokens { get; set; }

    public virtual DbSet<PairingsLeagueSeason> PairingsLeagueSeason { get; set; }

    public virtual DbSet<PersistedGrants> PersistedGrants { get; set; }

    public virtual DbSet<PersonContacts> PersonContacts { get; set; }

    public virtual DbSet<PushNotifications> PushNotifications { get; set; }

    public virtual DbSet<PushSubscriptionJobs> PushSubscriptionJobs { get; set; }

    public virtual DbSet<PushSubscriptionRegistrations> PushSubscriptionRegistrations { get; set; }

    public virtual DbSet<PushSubscriptionTeams> PushSubscriptionTeams { get; set; }

    public virtual DbSet<PushSubscriptions> PushSubscriptions { get; set; }

    public virtual DbSet<RefGameAssigments> RefGameAssigments { get; set; }

    public virtual DbSet<RegFormFieldOptions> RegFormFieldOptions { get; set; }

    public virtual DbSet<RegFormFieldTypes> RegFormFieldTypes { get; set; }

    public virtual DbSet<RegFormFields> RegFormFields { get; set; }

    public virtual DbSet<RegForms> RegForms { get; set; }

    public virtual DbSet<RegistrationAccounting> RegistrationAccounting { get; set; }

    public virtual DbSet<RegistrationFormPaymentMethods> RegistrationFormPaymentMethods { get; set; }

    public virtual DbSet<Registrations> Registrations { get; set; }

    public virtual DbSet<ReportExportTypes> ReportExportTypes { get; set; }

    public virtual DbSet<Reports> Reports { get; set; }

    public virtual DbSet<Schedule> Schedule { get; set; }

    public virtual DbSet<ScheduleTeamTypes> ScheduleTeamTypes { get; set; }

    public virtual DbSet<Sliders> Sliders { get; set; }

    public virtual DbSet<Slides> Slides { get; set; }

    public virtual DbSet<Sports> Sports { get; set; }

    public virtual DbSet<StandingsSortProfileRules> StandingsSortProfileRules { get; set; }

    public virtual DbSet<StandingsSortProfiles> StandingsSortProfiles { get; set; }

    public virtual DbSet<StandingsSortRules> StandingsSortRules { get; set; }

    public virtual DbSet<States> States { get; set; }

    public virtual DbSet<StoreCart> StoreCart { get; set; }

    public virtual DbSet<StoreCartBatchAccounting> StoreCartBatchAccounting { get; set; }

    public virtual DbSet<StoreCartBatchSkuEdits> StoreCartBatchSkuEdits { get; set; }

    public virtual DbSet<StoreCartBatchSkuQuantityAdjustments> StoreCartBatchSkuQuantityAdjustments { get; set; }

    public virtual DbSet<StoreCartBatchSkuRestocks> StoreCartBatchSkuRestocks { get; set; }

    public virtual DbSet<StoreCartBatchSkus> StoreCartBatchSkus { get; set; }

    public virtual DbSet<StoreCartBatches> StoreCartBatches { get; set; }

    public virtual DbSet<StoreColors> StoreColors { get; set; }

    public virtual DbSet<StoreItemSkus> StoreItemSkus { get; set; }

    public virtual DbSet<StoreItems> StoreItems { get; set; }

    public virtual DbSet<StoreSizes> StoreSizes { get; set; }

    public virtual DbSet<Stores> Stores { get; set; }

    public virtual DbSet<TeamAttendanceEvents> TeamAttendanceEvents { get; set; }

    public virtual DbSet<TeamAttendanceRecords> TeamAttendanceRecords { get; set; }

    public virtual DbSet<TeamAttendanceTypes> TeamAttendanceTypes { get; set; }

    public virtual DbSet<TeamDocs> TeamDocs { get; set; }

    public virtual DbSet<TeamEvents> TeamEvents { get; set; }

    public virtual DbSet<TeamGalleryPhotos> TeamGalleryPhotos { get; set; }

    public virtual DbSet<TeamMessages> TeamMessages { get; set; }

    public virtual DbSet<TeamRosterRequests> TeamRosterRequests { get; set; }

    public virtual DbSet<TeamSignupEvents> TeamSignupEvents { get; set; }

    public virtual DbSet<TeamSignupEventsRegistrations> TeamSignupEventsRegistrations { get; set; }

    public virtual DbSet<TeamTournamentMappings> TeamTournamentMappings { get; set; }

    public virtual DbSet<Teams> Teams { get; set; }

    public virtual DbSet<Themes> Themes { get; set; }

    public virtual DbSet<TimeslotsLeagueSeasonDates> TimeslotsLeagueSeasonDates { get; set; }

    public virtual DbSet<TimeslotsLeagueSeasonFields> TimeslotsLeagueSeasonFields { get; set; }

    public virtual DbSet<Timezones> Timezones { get; set; }

    public virtual DbSet<Txs> Txs { get; set; }

    public virtual DbSet<VItemsToUpdate> VItemsToUpdate { get; set; }

    public virtual DbSet<VMonthlyJobStats> VMonthlyJobStats { get; set; }

    public virtual DbSet<VRegistrationsSearch> VRegistrationsSearch { get; set; }

    public virtual DbSet<VTeamCacreview> VTeamCacreview { get; set; }

    public virtual DbSet<VTxs> VTxs { get; set; }

    public virtual DbSet<VerticalInsurePayouts> VerticalInsurePayouts { get; set; }

    public virtual DbSet<Widget> Widget { get; set; }

    public virtual DbSet<WidgetCategory> WidgetCategory { get; set; }

    public virtual DbSet<WidgetDefault> WidgetDefault { get; set; }

    public virtual DbSet<Yn2023schedule> Yn2023schedule { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountingApplyToSummaries>(entity =>
        {
            entity.HasKey(e => e.ApplyToId)
                .HasName("PK__Accounti__4440FD8A11CBD945")
                .HasFillFactor(60);

            entity.ToTable("Accounting_ApplyTo_Summaries", "adn");

            entity.Property(e => e.ApplyToId)
                .ValueGeneratedNever()
                .HasColumnName("applyToID");
            entity.Property(e => e.MaxAdntransactionId)
                .HasMaxLength(25)
                .IsUnicode(false)
                .HasColumnName("maxADNTransactionID");
            entity.Property(e => e.PayamtBase)
                .HasColumnType("money")
                .HasColumnName("payamt_base");
            entity.Property(e => e.PayamtDc)
                .HasColumnType("money")
                .HasColumnName("payamt_dc");
            entity.Property(e => e.PayamtDon)
                .HasColumnType("money")
                .HasColumnName("payamt_don");
            entity.Property(e => e.PayamtLf)
                .HasColumnType("money")
                .HasColumnName("payamt_lf");
            entity.Property(e => e.SumAmtPaid)
                .HasColumnType("money")
                .HasColumnName("sumAmtPaid");
            entity.Property(e => e.SumFees)
                .HasColumnType("money")
                .HasColumnName("sumFees");
            entity.Property(e => e.SumOwed)
                .HasColumnType("money")
                .HasColumnName("sumOwed");

            entity.HasOne(d => d.ApplyTo).WithOne(p => p.AccountingApplyToSummaries)
                .HasForeignKey<AccountingApplyToSummaries>(d => d.ApplyToId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Accounting_ApplyTo_Summaries_ApplyTo");
        });

        modelBuilder.Entity<AccountingPaymentMethods>(entity =>
        {
            entity.HasKey(e => e.PaymentMethodId).HasName("PK_reference.Accounting_PaymentMethods");

            entity.ToTable("Accounting_PaymentMethods", "reference");

            entity.Property(e => e.PaymentMethodId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("paymentMethodID");
            entity.Property(e => e.Ai)
                .ValueGeneratedOnAdd()
                .HasColumnName("ai");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.PaymentMethod).HasColumnName("paymentMethod");

            entity.HasOne(d => d.LebUser).WithMany(p => p.AccountingPaymentMethods)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_reference.Accounting_PaymentMethods_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<Adn0714And15Records>(entity =>
        {
            entity.HasNoKey();

            entity.Property(e => e.Address).HasMaxLength(50);
            entity.Property(e => e.AddressVerificationStatus)
                .IsUnicode(false)
                .HasColumnName("Address_Verification_Status");
            entity.Property(e => e.ApprovedAmount)
                .HasMaxLength(1)
                .HasColumnName("Approved_Amount");
            entity.Property(e => e.AuthorizationAmount).HasColumnName("Authorization_Amount");
            entity.Property(e => e.AuthorizationCode)
                .HasMaxLength(50)
                .HasColumnName("Authorization_Code");
            entity.Property(e => e.AuthorizationCurrency)
                .HasMaxLength(50)
                .HasColumnName("Authorization_Currency");
            entity.Property(e => e.AvailableCardBalance)
                .HasMaxLength(1)
                .HasColumnName("Available_Card_Balance");
            entity.Property(e => e.BankAccountNumber)
                .HasMaxLength(1)
                .HasColumnName("Bank_Account_Number");
            entity.Property(e => e.BusinessDay).HasColumnName("Business_Day");
            entity.Property(e => e.CardCodeStatus)
                .HasMaxLength(50)
                .HasColumnName("Card_Code_Status");
            entity.Property(e => e.CardNumber)
                .HasMaxLength(50)
                .HasColumnName("Card_Number");
            entity.Property(e => e.CavvResultsCode)
                .HasMaxLength(1)
                .HasColumnName("CAVV_Results_Code");
            entity.Property(e => e.City).HasMaxLength(1);
            entity.Property(e => e.Company).HasMaxLength(1);
            entity.Property(e => e.Country).HasMaxLength(1);
            entity.Property(e => e.Currency).HasMaxLength(50);
            entity.Property(e => e.CustomerFirstName)
                .HasMaxLength(50)
                .HasColumnName("Customer_First_Name");
            entity.Property(e => e.CustomerId)
                .HasMaxLength(1)
                .HasColumnName("Customer_ID");
            entity.Property(e => e.CustomerLastName)
                .HasMaxLength(50)
                .HasColumnName("Customer_Last_Name");
            entity.Property(e => e.Email).HasMaxLength(50);
            entity.Property(e => e.ExpirationDate)
                .HasMaxLength(50)
                .HasColumnName("Expiration_Date");
            entity.Property(e => e.Fax).HasMaxLength(1);
            entity.Property(e => e.FraudscreenApplied)
                .HasMaxLength(50)
                .HasColumnName("Fraudscreen_Applied");
            entity.Property(e => e.InvoiceDescription)
                .IsUnicode(false)
                .HasColumnName("Invoice_Description");
            entity.Property(e => e.InvoiceNumber)
                .HasMaxLength(50)
                .HasColumnName("Invoice_Number");
            entity.Property(e => e.L2Duty)
                .HasMaxLength(1)
                .HasColumnName("L2_Duty");
            entity.Property(e => e.L2Freight)
                .HasMaxLength(1)
                .HasColumnName("L2_Freight");
            entity.Property(e => e.L2PurchaseOrderNumber)
                .HasMaxLength(1)
                .HasColumnName("L2_Purchase_Order_Number");
            entity.Property(e => e.L2Tax)
                .HasMaxLength(1)
                .HasColumnName("L2_Tax");
            entity.Property(e => e.L2TaxExempt)
                .HasMaxLength(50)
                .HasColumnName("L2_Tax_Exempt");
            entity.Property(e => e.MarketType)
                .HasMaxLength(50)
                .HasColumnName("Market_Type");
            entity.Property(e => e.OrderNumber)
                .HasMaxLength(1)
                .HasColumnName("Order_Number");
            entity.Property(e => e.PartialCaptureStatus)
                .HasMaxLength(50)
                .HasColumnName("Partial_Capture_Status");
            entity.Property(e => e.Phone).HasMaxLength(1);
            entity.Property(e => e.Product).HasMaxLength(50);
            entity.Property(e => e.RecurringBillingTransaction)
                .HasMaxLength(50)
                .HasColumnName("Recurring_Billing_Transaction");
            entity.Property(e => e.ReferenceTransactionId).HasColumnName("Reference_Transaction_ID");
            entity.Property(e => e.Reserved10).HasMaxLength(1);
            entity.Property(e => e.Reserved11).HasMaxLength(1);
            entity.Property(e => e.Reserved12).HasMaxLength(1);
            entity.Property(e => e.Reserved13).HasMaxLength(1);
            entity.Property(e => e.Reserved14).HasMaxLength(1);
            entity.Property(e => e.Reserved15).HasMaxLength(1);
            entity.Property(e => e.Reserved16).HasMaxLength(1);
            entity.Property(e => e.Reserved17).HasMaxLength(1);
            entity.Property(e => e.Reserved18).HasMaxLength(1);
            entity.Property(e => e.Reserved19).HasMaxLength(1);
            entity.Property(e => e.Reserved20).HasMaxLength(1);
            entity.Property(e => e.Reserved7).HasMaxLength(1);
            entity.Property(e => e.Reserved8).HasMaxLength(1);
            entity.Property(e => e.Reserved9).HasMaxLength(1);
            entity.Property(e => e.RoutingNumber)
                .HasMaxLength(1)
                .HasColumnName("Routing_Number");
            entity.Property(e => e.SettlementAmount)
                .HasColumnType("money")
                .HasColumnName("Settlement_Amount");
            entity.Property(e => e.SettlementCurrency)
                .HasMaxLength(50)
                .HasColumnName("Settlement_Currency");
            entity.Property(e => e.SettlementDateTime)
                .HasMaxLength(50)
                .HasColumnName("Settlement_Date_Time");
            entity.Property(e => e.ShipToAddress)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_Address");
            entity.Property(e => e.ShipToCity)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_City");
            entity.Property(e => e.ShipToCompany)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_Company");
            entity.Property(e => e.ShipToCountry)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_Country");
            entity.Property(e => e.ShipToFirstName)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_First_Name");
            entity.Property(e => e.ShipToLastName)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_Last_Name");
            entity.Property(e => e.ShipToState)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_State");
            entity.Property(e => e.ShipToZip)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_ZIP");
            entity.Property(e => e.State).HasMaxLength(1);
            entity.Property(e => e.SubmitDateTime)
                .HasMaxLength(50)
                .HasColumnName("Submit_Date_Time");
            entity.Property(e => e.TotalAmount).HasColumnName("Total_Amount");
            entity.Property(e => e.TransactionId).HasColumnName("Transaction_ID");
            entity.Property(e => e.TransactionStatus)
                .HasMaxLength(50)
                .HasColumnName("Transaction_Status");
            entity.Property(e => e.TransactionType)
                .HasMaxLength(50)
                .HasColumnName("Transaction_Type");
            entity.Property(e => e.Zip)
                .HasMaxLength(50)
                .HasColumnName("ZIP");
        });

        modelBuilder.Entity<AdndataFromPhoenix>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ADNDataFromPhoenix");

            entity.Property(e => e.AId).HasColumnName("aID");
            entity.Property(e => e.Act)
                .HasMaxLength(255)
                .HasColumnName("act");
            entity.Property(e => e.Active).HasColumnName("active");
            entity.Property(e => e.AdnCc4).HasColumnName("adnCC4");
            entity.Property(e => e.AdnCcexpDate)
                .HasColumnType("datetime")
                .HasColumnName("adnCCExpDate");
            entity.Property(e => e.AdnInvoiceNo)
                .HasMaxLength(255)
                .HasColumnName("adnInvoiceNo");
            entity.Property(e => e.AdnSubscriptionAmountPerOccurence)
                .HasMaxLength(255)
                .HasColumnName("adnSubscriptionAmountPerOccurence");
            entity.Property(e => e.AdnSubscriptionBillingOccurences)
                .HasMaxLength(255)
                .HasColumnName("adnSubscriptionBillingOccurences");
            entity.Property(e => e.AdnSubscriptionId)
                .HasMaxLength(255)
                .HasColumnName("adnSubscriptionId");
            entity.Property(e => e.AdnSubscriptionIntervalLength).HasColumnName("adnSubscriptionIntervalLength");
            entity.Property(e => e.AdnSubscriptionStartDate)
                .HasMaxLength(255)
                .HasColumnName("adnSubscriptionStartDate");
            entity.Property(e => e.AdnSubscriptionStatus)
                .HasMaxLength(255)
                .HasColumnName("adnSubscriptionStatus");
            entity.Property(e => e.AdnTransactionId).HasColumnName("adnTransactionID");
            entity.Property(e => e.AssignedAgegroupId)
                .HasMaxLength(255)
                .HasColumnName("assigned_agegroupID");
            entity.Property(e => e.AssignedCustomerId)
                .HasMaxLength(255)
                .HasColumnName("assigned_customerID");
            entity.Property(e => e.AssignedDivId)
                .HasMaxLength(255)
                .HasColumnName("assigned_divID");
            entity.Property(e => e.AssignedLeagueId)
                .HasMaxLength(255)
                .HasColumnName("assigned_leagueID");
            entity.Property(e => e.AssignedTeamId)
                .HasMaxLength(255)
                .HasColumnName("assigned_teamID");
            entity.Property(e => e.Assignment)
                .HasMaxLength(255)
                .HasColumnName("assignment");
            entity.Property(e => e.BActive).HasColumnName("bActive");
            entity.Property(e => e.BBgcheck).HasColumnName("bBGCheck");
            entity.Property(e => e.BCollegeCommit).HasColumnName("bCollegeCommit");
            entity.Property(e => e.BConfirmationSent).HasColumnName("bConfirmationSent");
            entity.Property(e => e.BMedAlert).HasColumnName("bMedAlert");
            entity.Property(e => e.BScholarshipRequested).HasColumnName("bScholarshipRequested");
            entity.Property(e => e.BTravel).HasColumnName("bTravel");
            entity.Property(e => e.BUploadedInsuranceCard).HasColumnName("bUploadedInsuranceCard");
            entity.Property(e => e.BUploadedMedForm).HasColumnName("bUploadedMedForm");
            entity.Property(e => e.BUploadedVaccineCard).HasColumnName("bUploadedVaccineCard");
            entity.Property(e => e.BWaiverSigned1).HasColumnName("bWaiverSigned1");
            entity.Property(e => e.BWaiverSigned2).HasColumnName("bWaiverSigned2");
            entity.Property(e => e.BWaiverSigned3).HasColumnName("bWaiverSigned3");
            entity.Property(e => e.BWaiverSignedCv19).HasColumnName("bWaiverSignedCV19");
            entity.Property(e => e.BackcheckExplain)
                .HasMaxLength(255)
                .HasColumnName("backcheck_explain");
            entity.Property(e => e.BgCheckDate)
                .HasMaxLength(255)
                .HasColumnName("bgCheckDate");
            entity.Property(e => e.CampsAttending).HasMaxLength(255);
            entity.Property(e => e.CertDate)
                .HasMaxLength(255)
                .HasColumnName("certDate");
            entity.Property(e => e.CertNo)
                .HasMaxLength(255)
                .HasColumnName("certNo");
            entity.Property(e => e.CheckNo)
                .HasMaxLength(255)
                .HasColumnName("checkNo");
            entity.Property(e => e.ClassRank)
                .HasMaxLength(255)
                .HasColumnName("class_rank");
            entity.Property(e => e.ClubCoach)
                .HasMaxLength(255)
                .HasColumnName("club_coach");
            entity.Property(e => e.ClubCoachEmail)
                .HasMaxLength(255)
                .HasColumnName("club_coach_email");
            entity.Property(e => e.ClubName)
                .HasMaxLength(255)
                .HasColumnName("club_name");
            entity.Property(e => e.ClubTeamName).HasMaxLength(255);
            entity.Property(e => e.CollegeCommit)
                .HasMaxLength(255)
                .HasColumnName("college_commit");
            entity.Property(e => e.Comment)
                .HasMaxLength(255)
                .HasColumnName("comment");
            entity.Property(e => e.Createdate).HasColumnName("createdate");
            entity.Property(e => e.CustomerId)
                .HasMaxLength(255)
                .HasColumnName("CustomerID");
            entity.Property(e => e.DadInstagram)
                .HasMaxLength(255)
                .HasColumnName("Dad_Instagram");
            entity.Property(e => e.DadTwitter)
                .HasMaxLength(255)
                .HasColumnName("Dad_Twitter");
            entity.Property(e => e.DayGroup)
                .HasMaxLength(255)
                .HasColumnName("dayGroup");
            entity.Property(e => e.DiscountCodeAi).HasMaxLength(255);
            entity.Property(e => e.DiscountCodeId)
                .HasMaxLength(255)
                .HasColumnName("DiscountCodeID");
            entity.Property(e => e.Dueamt).HasColumnName("dueamt");
            entity.Property(e => e.FamilyUserId)
                .HasMaxLength(255)
                .HasColumnName("Family_UserId");
            entity.Property(e => e.Fastestshot)
                .HasMaxLength(255)
                .HasColumnName("fastestshot");
            entity.Property(e => e.FeeBase).HasColumnName("fee_base");
            entity.Property(e => e.FeeDiscount).HasColumnName("fee_discount");
            entity.Property(e => e.FeeDiscountMp).HasColumnName("fee_discount_mp");
            entity.Property(e => e.FeeDonation).HasColumnName("fee_donation");
            entity.Property(e => e.FeeLatefee).HasColumnName("fee_latefee");
            entity.Property(e => e.FeeProcessing).HasColumnName("fee_processing");
            entity.Property(e => e.FeeTotal).HasColumnName("fee_total");
            entity.Property(e => e.FiveTenFive)
                .HasMaxLength(255)
                .HasColumnName("five_ten_five");
            entity.Property(e => e.Fourtyyarddash)
                .HasMaxLength(255)
                .HasColumnName("fourtyyarddash");
            entity.Property(e => e.Gloves)
                .HasMaxLength(255)
                .HasColumnName("gloves");
            entity.Property(e => e.Gpa)
                .HasMaxLength(255)
                .HasColumnName("gpa");
            entity.Property(e => e.GradYear).HasColumnName("grad_year");
            entity.Property(e => e.HeadshotPath)
                .HasMaxLength(255)
                .HasColumnName("headshot_path");
            entity.Property(e => e.HealthInsurer)
                .HasMaxLength(255)
                .HasColumnName("health_insurer");
            entity.Property(e => e.HealthInsurerGroupNo)
                .HasMaxLength(255)
                .HasColumnName("health_insurer_group_no");
            entity.Property(e => e.HealthInsurerPhone)
                .HasMaxLength(255)
                .HasColumnName("health_insurer_phone");
            entity.Property(e => e.HealthInsurerPolicyNo)
                .HasMaxLength(255)
                .HasColumnName("health_insurer_policy_no");
            entity.Property(e => e.Height)
                .HasMaxLength(255)
                .HasColumnName("height");
            entity.Property(e => e.HeightInches)
                .HasMaxLength(255)
                .HasColumnName("height_inches");
            entity.Property(e => e.Honors).HasMaxLength(255);
            entity.Property(e => e.HonorsAcademic)
                .HasMaxLength(255)
                .HasColumnName("honors_academic");
            entity.Property(e => e.HonorsAthletic)
                .HasMaxLength(255)
                .HasColumnName("honors_athletic");
            entity.Property(e => e.Instagram).HasMaxLength(255);
            entity.Property(e => e.InsuredName)
                .HasMaxLength(255)
                .HasColumnName("insured_name");
            entity.Property(e => e.JerseySize)
                .HasMaxLength(255)
                .HasColumnName("jersey_size");
            entity.Property(e => e.JobId)
                .HasMaxLength(255)
                .HasColumnName("jobID");
            entity.Property(e => e.Kilt)
                .HasMaxLength(255)
                .HasColumnName("kilt");
            entity.Property(e => e.LeaguesAttending).HasMaxLength(255);
            entity.Property(e => e.LebUserId)
                .HasMaxLength(255)
                .HasColumnName("lebUserID");
            entity.Property(e => e.LebUserId3)
                .HasMaxLength(255)
                .HasColumnName("lebUserID3");
            entity.Property(e => e.MedicalNote)
                .HasMaxLength(255)
                .HasColumnName("medical_note");
            entity.Property(e => e.Modified).HasColumnName("modified");
            entity.Property(e => e.Modified4).HasColumnName("modified4");
            entity.Property(e => e.ModifiedMobile)
                .HasMaxLength(255)
                .HasColumnName("modified_mobile");
            entity.Property(e => e.MomInstagram)
                .HasMaxLength(255)
                .HasColumnName("Mom_Instagram");
            entity.Property(e => e.MomTwitter)
                .HasMaxLength(255)
                .HasColumnName("Mom_Twitter");
            entity.Property(e => e.NightGroup)
                .HasMaxLength(255)
                .HasColumnName("nightGroup");
            entity.Property(e => e.OtherSports)
                .HasMaxLength(255)
                .HasColumnName("other_sports");
            entity.Property(e => e.OwedTotal).HasColumnName("owed_total");
            entity.Property(e => e.PaidTotal).HasColumnName("paid_total");
            entity.Property(e => e.Payamt).HasColumnName("payamt");
            entity.Property(e => e.PaymentMethodChosen).HasMaxLength(255);
            entity.Property(e => e.PaymentMethodId)
                .HasMaxLength(255)
                .HasColumnName("paymentMethodID");
            entity.Property(e => e.Paymeth)
                .HasMaxLength(255)
                .HasColumnName("paymeth");
            entity.Property(e => e.Position)
                .HasMaxLength(255)
                .HasColumnName("position");
            entity.Property(e => e.PreviousCoach1).HasMaxLength(255);
            entity.Property(e => e.PreviousCoach2).HasMaxLength(255);
            entity.Property(e => e.PromoCode)
                .HasMaxLength(255)
                .HasColumnName("promoCode");
            entity.Property(e => e.Psat)
                .HasMaxLength(255)
                .HasColumnName("psat");
            entity.Property(e => e.RegformId)
                .HasMaxLength(255)
                .HasColumnName("regformId");
            entity.Property(e => e.Region)
                .HasMaxLength(255)
                .HasColumnName("region");
            entity.Property(e => e.RegistrationAi).HasColumnName("RegistrationAI");
            entity.Property(e => e.RegistrationCategory).HasMaxLength(255);
            entity.Property(e => e.RegistrationFormName)
                .HasMaxLength(255)
                .HasColumnName("registrationFormName");
            entity.Property(e => e.RegistrationGroupId)
                .HasMaxLength(255)
                .HasColumnName("RegistrationGroupID");
            entity.Property(e => e.RegistrationId)
                .HasMaxLength(255)
                .HasColumnName("RegistrationID");
            entity.Property(e => e.RegistrationId2)
                .HasMaxLength(255)
                .HasColumnName("RegistrationID2");
            entity.Property(e => e.RegistrationTs).HasColumnName("RegistrationTS");
            entity.Property(e => e.RequestedAgegroupId)
                .HasMaxLength(255)
                .HasColumnName("requestedAgegroupID");
            entity.Property(e => e.Reversible)
                .HasMaxLength(255)
                .HasColumnName("reversible");
            entity.Property(e => e.RoleId).HasMaxLength(255);
            entity.Property(e => e.RoommatePref)
                .HasMaxLength(255)
                .HasColumnName("roommate_pref");
            entity.Property(e => e.Sat)
                .HasMaxLength(255)
                .HasColumnName("sat");
            entity.Property(e => e.SatMath)
                .HasMaxLength(255)
                .HasColumnName("satMath");
            entity.Property(e => e.SatVerbal)
                .HasMaxLength(255)
                .HasColumnName("satVerbal");
            entity.Property(e => e.SatWriting)
                .HasMaxLength(255)
                .HasColumnName("satWriting");
            entity.Property(e => e.SchoolActivities)
                .HasMaxLength(255)
                .HasColumnName("school_activities");
            entity.Property(e => e.SchoolCoach)
                .HasMaxLength(255)
                .HasColumnName("school_coach");
            entity.Property(e => e.SchoolCoachEmail)
                .HasMaxLength(255)
                .HasColumnName("school_coach_email");
            entity.Property(e => e.SchoolGrade)
                .HasMaxLength(255)
                .HasColumnName("school_grade");
            entity.Property(e => e.SchoolLevelClasses)
                .HasMaxLength(255)
                .HasColumnName("school_level_classes");
            entity.Property(e => e.SchoolName)
                .HasMaxLength(255)
                .HasColumnName("school_name");
            entity.Property(e => e.SchoolTeamName)
                .HasMaxLength(255)
                .HasColumnName("school_team_name");
            entity.Property(e => e.Shoes)
                .HasMaxLength(255)
                .HasColumnName("shoes");
            entity.Property(e => e.ShortsSize)
                .HasMaxLength(255)
                .HasColumnName("shorts_size");
            entity.Property(e => e.SkillLevel)
                .HasMaxLength(255)
                .HasColumnName("skill_level");
            entity.Property(e => e.Snapchat).HasMaxLength(255);
            entity.Property(e => e.SpecialRequests)
                .HasMaxLength(255)
                .HasColumnName("specialRequests");
            entity.Property(e => e.SportAssnId)
                .HasMaxLength(255)
                .HasColumnName("sportAssnID");
            entity.Property(e => e.SportAssnIdexpDate)
                .HasMaxLength(255)
                .HasColumnName("sportAssnIDExpDate");
            entity.Property(e => e.SportYearsExp)
                .HasMaxLength(255)
                .HasColumnName("sport_years_exp");
            entity.Property(e => e.StrongHand)
                .HasMaxLength(255)
                .HasColumnName("strong_hand");
            entity.Property(e => e.Sweatpants)
                .HasMaxLength(255)
                .HasColumnName("sweatpants");
            entity.Property(e => e.Sweatshirt)
                .HasMaxLength(255)
                .HasColumnName("sweatshirt");
            entity.Property(e => e.TShirt)
                .HasMaxLength(255)
                .HasColumnName("t-shirt");
            entity.Property(e => e.TeamId)
                .HasMaxLength(255)
                .HasColumnName("teamID");
            entity.Property(e => e.Threehundredshuttle)
                .HasMaxLength(255)
                .HasColumnName("threehundredshuttle");
            entity.Property(e => e.Twitter).HasMaxLength(255);
            entity.Property(e => e.UniformNo)
                .HasMaxLength(255)
                .HasColumnName("uniform_no");
            entity.Property(e => e.UserId).HasMaxLength(255);
            entity.Property(e => e.VolChildreninprogram)
                .HasMaxLength(255)
                .HasColumnName("vol_childreninprogram");
            entity.Property(e => e.Volposition)
                .HasMaxLength(255)
                .HasColumnName("volposition");
            entity.Property(e => e.WeightLbs)
                .HasMaxLength(255)
                .HasColumnName("weight_lbs");
            entity.Property(e => e.WhoReferred).HasMaxLength(255);
        });

        modelBuilder.Entity<AdntripleThreatData>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ADNTripleThreatData");

            entity.Property(e => e.Address).HasMaxLength(50);
            entity.Property(e => e.AddressVerificationStatus)
                .HasMaxLength(100)
                .HasColumnName("Address_Verification_Status");
            entity.Property(e => e.ApprovedAmount)
                .HasMaxLength(1)
                .HasColumnName("Approved_Amount");
            entity.Property(e => e.AuthorizationAmount)
                .HasColumnType("money")
                .HasColumnName("Authorization_Amount");
            entity.Property(e => e.AuthorizationCode)
                .HasMaxLength(50)
                .HasColumnName("Authorization_Code");
            entity.Property(e => e.AuthorizationCurrency)
                .HasMaxLength(50)
                .HasColumnName("Authorization_Currency");
            entity.Property(e => e.AvailableCardBalance)
                .HasMaxLength(1)
                .HasColumnName("Available_Card_Balance");
            entity.Property(e => e.BankAccountNumber)
                .HasMaxLength(1)
                .HasColumnName("Bank_Account_Number");
            entity.Property(e => e.BusinessDay).HasColumnName("Business_Day");
            entity.Property(e => e.CardCodeStatus)
                .HasMaxLength(50)
                .HasColumnName("Card_Code_Status");
            entity.Property(e => e.CardNumber)
                .HasMaxLength(50)
                .HasColumnName("Card_Number");
            entity.Property(e => e.CavvResultsCode)
                .HasMaxLength(1)
                .HasColumnName("CAVV_Results_Code");
            entity.Property(e => e.City).HasMaxLength(1);
            entity.Property(e => e.Company).HasMaxLength(1);
            entity.Property(e => e.Country).HasMaxLength(1);
            entity.Property(e => e.Currency).HasMaxLength(50);
            entity.Property(e => e.CustomerFirstName)
                .HasMaxLength(50)
                .HasColumnName("Customer_First_Name");
            entity.Property(e => e.CustomerId)
                .HasMaxLength(1)
                .HasColumnName("Customer_ID");
            entity.Property(e => e.CustomerLastName)
                .HasMaxLength(50)
                .HasColumnName("Customer_Last_Name");
            entity.Property(e => e.Email).HasMaxLength(50);
            entity.Property(e => e.ExpirationDate)
                .HasMaxLength(50)
                .HasColumnName("Expiration_Date");
            entity.Property(e => e.Fax).HasMaxLength(1);
            entity.Property(e => e.FraudscreenApplied)
                .HasMaxLength(50)
                .HasColumnName("Fraudscreen_Applied");
            entity.Property(e => e.InvoiceDescription)
                .IsUnicode(false)
                .HasColumnName("Invoice_Description");
            entity.Property(e => e.InvoiceNumber)
                .HasMaxLength(50)
                .HasColumnName("Invoice_Number");
            entity.Property(e => e.L2Duty)
                .HasMaxLength(1)
                .HasColumnName("L2_Duty");
            entity.Property(e => e.L2Freight)
                .HasMaxLength(1)
                .HasColumnName("L2_Freight");
            entity.Property(e => e.L2PurchaseOrderNumber)
                .HasMaxLength(1)
                .HasColumnName("L2_Purchase_Order_Number");
            entity.Property(e => e.L2Tax)
                .HasMaxLength(1)
                .HasColumnName("L2_Tax");
            entity.Property(e => e.L2TaxExempt)
                .HasMaxLength(50)
                .HasColumnName("L2_Tax_Exempt");
            entity.Property(e => e.MarketType)
                .HasMaxLength(50)
                .HasColumnName("Market_Type");
            entity.Property(e => e.OrderNumber)
                .HasMaxLength(1)
                .HasColumnName("Order_Number");
            entity.Property(e => e.PartialCaptureStatus)
                .HasMaxLength(50)
                .HasColumnName("Partial_Capture_Status");
            entity.Property(e => e.Phone).HasMaxLength(1);
            entity.Property(e => e.Product).HasMaxLength(50);
            entity.Property(e => e.RecurringBillingTransaction)
                .HasMaxLength(50)
                .HasColumnName("Recurring_Billing_Transaction");
            entity.Property(e => e.ReferenceTransactionId).HasColumnName("Reference_Transaction_ID");
            entity.Property(e => e.Reserved10).HasMaxLength(1);
            entity.Property(e => e.Reserved11).HasMaxLength(1);
            entity.Property(e => e.Reserved12).HasMaxLength(1);
            entity.Property(e => e.Reserved13).HasMaxLength(1);
            entity.Property(e => e.Reserved14).HasMaxLength(1);
            entity.Property(e => e.Reserved15).HasMaxLength(1);
            entity.Property(e => e.Reserved16).HasMaxLength(1);
            entity.Property(e => e.Reserved17).HasMaxLength(1);
            entity.Property(e => e.Reserved18).HasMaxLength(1);
            entity.Property(e => e.Reserved19).HasMaxLength(1);
            entity.Property(e => e.Reserved20).HasMaxLength(1);
            entity.Property(e => e.Reserved7).HasMaxLength(1);
            entity.Property(e => e.Reserved8).HasMaxLength(1);
            entity.Property(e => e.Reserved9).HasMaxLength(1);
            entity.Property(e => e.RoutingNumber)
                .HasMaxLength(1)
                .HasColumnName("Routing_Number");
            entity.Property(e => e.SettlementAmount).HasColumnName("Settlement_Amount");
            entity.Property(e => e.SettlementCurrency)
                .HasMaxLength(50)
                .HasColumnName("Settlement_Currency");
            entity.Property(e => e.SettlementDateTime)
                .HasMaxLength(50)
                .HasColumnName("Settlement_Date_Time");
            entity.Property(e => e.ShipToAddress)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_Address");
            entity.Property(e => e.ShipToCity)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_City");
            entity.Property(e => e.ShipToCompany)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_Company");
            entity.Property(e => e.ShipToCountry)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_Country");
            entity.Property(e => e.ShipToFirstName)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_First_Name");
            entity.Property(e => e.ShipToLastName)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_Last_Name");
            entity.Property(e => e.ShipToState)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_State");
            entity.Property(e => e.ShipToZip)
                .HasMaxLength(1)
                .HasColumnName("Ship_To_ZIP");
            entity.Property(e => e.State).HasMaxLength(1);
            entity.Property(e => e.SubmitDateTime)
                .HasMaxLength(50)
                .HasColumnName("Submit_Date_Time");
            entity.Property(e => e.TotalAmount).HasColumnName("Total_Amount");
            entity.Property(e => e.TransactionId).HasColumnName("Transaction_ID");
            entity.Property(e => e.TransactionStatus).HasColumnName("Transaction_Status");
            entity.Property(e => e.TransactionType)
                .HasMaxLength(50)
                .HasColumnName("Transaction_Type");
            entity.Property(e => e.Zip)
                .HasMaxLength(50)
                .HasColumnName("ZIP");
        });

        modelBuilder.Entity<Agegroups>(entity =>
        {
            entity.HasKey(e => e.AgegroupId).HasName("PK_Leagues.agegroups");

            entity.ToTable("agegroups", "Leagues");

            entity.Property(e => e.AgegroupId)
                .HasDefaultValueSql("(newsequentialid())", "DF__agegroups__agegr__21C0F255")
                .HasColumnName("agegroupID");
            entity.Property(e => e.AgegroupName).HasColumnName("agegroupName");
            entity.Property(e => e.BAllowApiRosterAccess)
                .HasDefaultValue(false)
                .HasColumnName("bAllowApiRosterAccess");
            entity.Property(e => e.BAllowSelfRostering)
                .HasDefaultValue(false, "DF_agegroups_bAllowSelfRostering")
                .HasColumnName("bAllowSelfRostering");
            entity.Property(e => e.BChampionsByDivision)
                .HasDefaultValue(false)
                .HasColumnName("bChampionsByDivision");
            entity.Property(e => e.BHideStandings)
                .HasDefaultValue(false)
                .HasColumnName("bHideStandings");
            entity.Property(e => e.Color).HasColumnName("color");
            entity.Property(e => e.DiscountFee)
                .HasColumnType("money")
                .HasColumnName("discountFee");
            entity.Property(e => e.DiscountFeeEnd).HasColumnName("discountFeeEnd");
            entity.Property(e => e.DiscountFeeStart).HasColumnName("discountFeeStart");
            entity.Property(e => e.DobMax).HasColumnName("dobMax");
            entity.Property(e => e.DobMin).HasColumnName("dobMin");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.GradYearMax).HasColumnName("grad_year_max");
            entity.Property(e => e.GradYearMin).HasColumnName("grad_year_min");
            entity.Property(e => e.LateFee)
                .HasColumnType("money")
                .HasColumnName("lateFee");
            entity.Property(e => e.LateFeeEnd).HasColumnName("lateFeeEnd");
            entity.Property(e => e.LateFeeStart).HasColumnName("lateFeeStart");
            entity.Property(e => e.LeagueId).HasColumnName("leagueID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.MaxTeams)
                .HasDefaultValue(100, "DF__agegroups__maxTe__22B5168E")
                .HasColumnName("maxTeams");
            entity.Property(e => e.MaxTeamsPerClub)
                .HasDefaultValue(100, "DF__agegroups__maxTe__23A93AC7")
                .HasColumnName("maxTeamsPerClub");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())", "DF__agegroups__modif__249D5F00")
                .HasColumnName("modified");
            entity.Property(e => e.PlayerFeeOverride).HasColumnType("money");
            entity.Property(e => e.RosterFee)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("rosterFee");
            entity.Property(e => e.RosterFeeLabel).HasColumnName("rosterFeeLabel");
            entity.Property(e => e.SchoolGradeMax).HasColumnName("school_grade_max");
            entity.Property(e => e.SchoolGradeMin).HasColumnName("school_grade_min");
            entity.Property(e => e.Season).HasColumnName("season");
            entity.Property(e => e.SortAge).HasColumnName("sortAge");
            entity.Property(e => e.TeamFee)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("teamFee");
            entity.Property(e => e.TeamFeeLabel).HasColumnName("teamFeeLabel");

            entity.HasOne(d => d.League).WithMany(p => p.Agegroups)
                .HasForeignKey(d => d.LeagueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Leagues.agegroups_Leagues.leagues_leagueID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Agegroups)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Leagues.agegroups_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<ApiClaims>(entity =>
        {
            entity.HasIndex(e => e.ApiResourceId, "IX_ApiClaims_ApiResourceId");

            entity.Property(e => e.Type).HasMaxLength(200);

            entity.HasOne(d => d.ApiResource).WithMany(p => p.ApiClaims).HasForeignKey(d => d.ApiResourceId);
        });

        modelBuilder.Entity<ApiProperties>(entity =>
        {
            entity.HasIndex(e => e.ApiResourceId, "IX_ApiProperties_ApiResourceId");

            entity.Property(e => e.Key).HasMaxLength(250);
            entity.Property(e => e.Value).HasMaxLength(2000);

            entity.HasOne(d => d.ApiResource).WithMany(p => p.ApiProperties).HasForeignKey(d => d.ApiResourceId);
        });

        modelBuilder.Entity<ApiResources>(entity =>
        {
            entity.HasIndex(e => e.Name, "IX_ApiResources_Name").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<ApiRosterPlayersAccessed>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__ApiRoste__3214EC0772974B5C");

            entity.ToTable("ApiRosterPlayersAccessed", "api");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ApiUserId).HasMaxLength(450);
            entity.Property(e => e.WhenAccessed).HasColumnType("datetime");

            entity.HasOne(d => d.ApiUser).WithMany(p => p.ApiRosterPlayersAccessed)
                .HasForeignKey(d => d.ApiUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ApiRoster__ApiUs__25B31578");

            entity.HasOne(d => d.Registration).WithMany(p => p.ApiRosterPlayersAccessed)
                .HasForeignKey(d => d.RegistrationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ApiRoster__Regis__26A739B1");
        });

        modelBuilder.Entity<ApiScopeClaims>(entity =>
        {
            entity.HasIndex(e => e.ApiScopeId, "IX_ApiScopeClaims_ApiScopeId");

            entity.Property(e => e.Type).HasMaxLength(200);

            entity.HasOne(d => d.ApiScope).WithMany(p => p.ApiScopeClaims).HasForeignKey(d => d.ApiScopeId);
        });

        modelBuilder.Entity<ApiScopes>(entity =>
        {
            entity.HasIndex(e => e.ApiResourceId, "IX_ApiScopes_ApiResourceId");

            entity.HasIndex(e => e.Name, "IX_ApiScopes_Name").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Name).HasMaxLength(200);

            entity.HasOne(d => d.ApiResource).WithMany(p => p.ApiScopes).HasForeignKey(d => d.ApiResourceId);
        });

        modelBuilder.Entity<ApiSecrets>(entity =>
        {
            entity.HasIndex(e => e.ApiResourceId, "IX_ApiSecrets_ApiResourceId");

            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Type).HasMaxLength(250);
            entity.Property(e => e.Value).HasMaxLength(4000);

            entity.HasOne(d => d.ApiResource).WithMany(p => p.ApiSecrets).HasForeignKey(d => d.ApiResourceId);
        });

        modelBuilder.Entity<AspNetRoleClaims>(entity =>
        {
            entity.Property(e => e.RoleId).HasMaxLength(450);

            entity.HasOne(d => d.Role).WithMany(p => p.AspNetRoleClaims).HasForeignKey(d => d.RoleId);
        });

        modelBuilder.Entity<AspNetUserClaims>(entity =>
        {
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserClaims).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserLogins>(entity =>
        {
            entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });

            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserLogins).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUsers>(entity =>
        {
            entity.Property(e => e.BTsicwaiverSigned).HasColumnName("bTSICWaiverSigned");
            entity.Property(e => e.Cellphone).HasColumnName("cellphone");
            entity.Property(e => e.CellphoneProvider).HasColumnName("cellphoneProvider");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.CreateDate).HasColumnType("datetime");
            entity.Property(e => e.Dob).HasColumnName("dob");
            entity.Property(e => e.Fax).HasColumnName("fax");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.ImageFile).HasColumnName("imageFile");
            entity.Property(e => e.ImageFileMimeType).HasColumnName("imageFileMimeType");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())", "DF_AspNetUsers_modified")
                .HasColumnName("modified");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.PostalCode).HasColumnName("postalCode");
            entity.Property(e => e.State).HasColumnName("state");
            entity.Property(e => e.StreetAddress).HasColumnName("streetAddress");
            entity.Property(e => e.StsHasUnsubscribed).HasColumnName("stsHasUnsubscribed");
            entity.Property(e => e.TsicwaiverSignedTs).HasColumnName("TSICWaiverSigned_TS");
            entity.Property(e => e.Workphone).HasColumnName("workphone");

            entity.HasMany(d => d.Role).WithMany(p => p.User)
                .UsingEntity<Dictionary<string, object>>(
                    "AspNetUserRoles",
                    r => r.HasOne<AspNetRoles>().WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.ClientSetNull),
                    l => l.HasOne<AspNetUsers>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                    });
        });

        modelBuilder.Entity<BillingTypes>(entity =>
        {
            entity.HasKey(e => e.BillingTypeId).HasName("PK_reference.Billing_Types");

            entity.ToTable("Billing_Types", "reference");

            entity.Property(e => e.BillingTypeId).HasColumnName("BillingTypeID");
            entity.Property(e => e.JobTypeDesc).HasColumnName("jobTypeDesc");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.LebUser).WithMany(p => p.BillingTypes)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_reference.Billing_Types_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<BracketDataSingleElimination>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__BracketD__3214EC070D2A2F3D");

            entity.ToTable("BracketDataSingleElimination", "reference");

            entity.Property(e => e.RoundType)
                .HasMaxLength(10)
                .IsUnicode(false);
        });

        modelBuilder.Entity<BracketSeeds>(entity =>
        {
            entity.HasKey(e => e.AId).HasName("PK__BracketS__DE518A06C7716ABA");

            entity.ToTable("BracketSeeds", "Leagues");

            entity.Property(e => e.AId).HasColumnName("aId");
            entity.Property(e => e.Gid).HasColumnName("gid");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.T1SeedDivId).HasColumnName("t1SeedDivId");
            entity.Property(e => e.T1SeedRank).HasColumnName("t1SeedRank");
            entity.Property(e => e.T2SeedDivId).HasColumnName("t2SeedDivId");
            entity.Property(e => e.T2SeedRank).HasColumnName("t2SeedRank");
            entity.Property(e => e.WhichSide).HasColumnName("whichSide");

            entity.HasOne(d => d.GidNavigation).WithMany(p => p.BracketSeeds)
                .HasForeignKey(d => d.Gid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__BracketSeed__gid__1C7EB0F9");

            entity.HasOne(d => d.LebUser).WithMany(p => p.BracketSeeds)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK__BracketSe__lebUs__1F5B1DA4");

            entity.HasOne(d => d.T1SeedDiv).WithMany(p => p.BracketSeedsT1SeedDiv)
                .HasForeignKey(d => d.T1SeedDivId)
                .HasConstraintName("FK__BracketSe__t1See__1D72D532");

            entity.HasOne(d => d.T2SeedDiv).WithMany(p => p.BracketSeedsT2SeedDiv)
                .HasForeignKey(d => d.T2SeedDivId)
                .HasConstraintName("FK__BracketSe__t2See__1E66F96B");
        });

        modelBuilder.Entity<Bulletins>(entity =>
        {
            entity.HasKey(e => e.BulletinId).HasName("PK_Jobs.Bulletins");

            entity.ToTable("Bulletins", "Jobs");

            entity.Property(e => e.BulletinId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("bulletinID");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.Bcore).HasColumnName("BCore");
            entity.Property(e => e.CreateDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("createDate");
            entity.Property(e => e.EndDate).HasColumnType("datetime");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.StartDate).HasColumnType("datetime");
            entity.Property(e => e.Text).HasColumnName("text");
            entity.Property(e => e.Title).HasColumnName("title");

            entity.HasOne(d => d.Job).WithMany(p => p.Bulletins)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Bulletins_Jobs.Jobs_jobID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Bulletins)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.Bulletins_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<CalendarEvents>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__Calendar__7944C8107EAB9CFE");

            entity.ToTable("CalendarEvents", "Calendar");

            entity.Property(e => e.Description).IsUnicode(false);
            entity.Property(e => e.EventColor)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.LebUserId).HasMaxLength(450);
            entity.Property(e => e.Location).IsUnicode(false);
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.RecurrenceException).IsUnicode(false);
            entity.Property(e => e.RecurrenceRule).IsUnicode(false);
            entity.Property(e => e.Subject).IsUnicode(false);

            entity.HasOne(d => d.Agegroup).WithMany(p => p.CalendarEvents)
                .HasForeignKey(d => d.AgegroupId)
                .HasConstraintName("FK__CalendarE__Agegr__57555E04");

            entity.HasOne(d => d.Job).WithMany(p => p.CalendarEvents)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CalendarE__JobId__566139CB");

            entity.HasOne(d => d.LebUser).WithMany(p => p.CalendarEvents)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CalendarE__LebUs__5A31CAAF");

            entity.HasOne(d => d.Team).WithMany(p => p.CalendarEvents)
                .HasForeignKey(d => d.TeamId)
                .HasConstraintName("FK__CalendarE__TeamI__5849823D");
        });

        modelBuilder.Entity<CellphonecarrierDomains>(entity =>
        {
            entity.HasKey(e => e.Carrier).HasName("PK_reference.cellphonecarrier_domains");

            entity.ToTable("cellphonecarrier_domains", "reference");

            entity.Property(e => e.Carrier).HasColumnName("carrier");
            entity.Property(e => e.Aid)
                .ValueGeneratedOnAdd()
                .HasColumnName("aid");
            entity.Property(e => e.Domain).HasColumnName("domain");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.LebUser).WithMany(p => p.CellphonecarrierDomains)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_cellphonecarrier_domains_AspNetUsers");
        });

        modelBuilder.Entity<Charlieamericanselectconsolations2021>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("charlieamericanselectconsolations2021");

            entity.Property(e => e.FName).HasColumnName("fName");
            entity.Property(e => e.GDate)
                .HasColumnType("datetime")
                .HasColumnName("G_Date");
            entity.Property(e => e.Gid)
                .ValueGeneratedOnAdd()
                .HasColumnName("GID");
            entity.Property(e => e.T1Ann)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("t1_ann");
            entity.Property(e => e.T1Id).HasColumnName("T1_ID");
            entity.Property(e => e.T1Name)
                .IsUnicode(false)
                .HasColumnName("T1_Name");
            entity.Property(e => e.T1NameActual).HasColumnName("t1_name_Actual");
            entity.Property(e => e.T2Ann)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("t2_ann");
            entity.Property(e => e.T2Id).HasColumnName("T2_ID");
            entity.Property(e => e.T2Name)
                .IsUnicode(false)
                .HasColumnName("T2_Name");
            entity.Property(e => e.T2NmeActual).HasColumnName("t2_nme_Actual");
        });

        modelBuilder.Entity<ChatMessages>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("PK__ChatMess__C87C0C9CC2B559E8");

            entity.ToTable("ChatMessages", "chat");

            entity.Property(e => e.MessageId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Created)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatorUserId).HasMaxLength(450);
            entity.Property(e => e.Message).IsUnicode(false);

            entity.HasOne(d => d.CreatorUser).WithMany(p => p.ChatMessages)
                .HasForeignKey(d => d.CreatorUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ChatMessa__Creat__57356273");

            entity.HasOne(d => d.Team).WithMany(p => p.ChatMessages)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ChatMessa__TeamI__56413E3A");
        });

        modelBuilder.Entity<ClientClaims>(entity =>
        {
            entity.HasIndex(e => e.ClientId, "IX_ClientClaims_ClientId");

            entity.Property(e => e.Type).HasMaxLength(250);
            entity.Property(e => e.Value).HasMaxLength(250);

            entity.HasOne(d => d.Client).WithMany(p => p.ClientClaims).HasForeignKey(d => d.ClientId);
        });

        modelBuilder.Entity<ClientCorsOrigins>(entity =>
        {
            entity.HasIndex(e => e.ClientId, "IX_ClientCorsOrigins_ClientId");

            entity.Property(e => e.Origin).HasMaxLength(150);

            entity.HasOne(d => d.Client).WithMany(p => p.ClientCorsOrigins).HasForeignKey(d => d.ClientId);
        });

        modelBuilder.Entity<ClientGrantTypes>(entity =>
        {
            entity.HasIndex(e => e.ClientId, "IX_ClientGrantTypes_ClientId");

            entity.Property(e => e.GrantType).HasMaxLength(250);

            entity.HasOne(d => d.Client).WithMany(p => p.ClientGrantTypes).HasForeignKey(d => d.ClientId);
        });

        modelBuilder.Entity<ClientIdPrestrictions>(entity =>
        {
            entity.ToTable("ClientIdPRestrictions");

            entity.HasIndex(e => e.ClientId, "IX_ClientIdPRestrictions_ClientId");

            entity.Property(e => e.Provider).HasMaxLength(200);

            entity.HasOne(d => d.Client).WithMany(p => p.ClientIdPrestrictions).HasForeignKey(d => d.ClientId);
        });

        modelBuilder.Entity<ClientPostLogoutRedirectUris>(entity =>
        {
            entity.HasIndex(e => e.ClientId, "IX_ClientPostLogoutRedirectUris_ClientId");

            entity.Property(e => e.PostLogoutRedirectUri).HasMaxLength(2000);

            entity.HasOne(d => d.Client).WithMany(p => p.ClientPostLogoutRedirectUris).HasForeignKey(d => d.ClientId);
        });

        modelBuilder.Entity<ClientProperties>(entity =>
        {
            entity.HasIndex(e => e.ClientId, "IX_ClientProperties_ClientId");

            entity.Property(e => e.Key).HasMaxLength(250);
            entity.Property(e => e.Value).HasMaxLength(2000);

            entity.HasOne(d => d.Client).WithMany(p => p.ClientProperties).HasForeignKey(d => d.ClientId);
        });

        modelBuilder.Entity<ClientRedirectUris>(entity =>
        {
            entity.HasIndex(e => e.ClientId, "IX_ClientRedirectUris_ClientId");

            entity.Property(e => e.RedirectUri).HasMaxLength(2000);

            entity.HasOne(d => d.Client).WithMany(p => p.ClientRedirectUris).HasForeignKey(d => d.ClientId);
        });

        modelBuilder.Entity<ClientScopes>(entity =>
        {
            entity.HasIndex(e => e.ClientId, "IX_ClientScopes_ClientId");

            entity.Property(e => e.Scope).HasMaxLength(200);

            entity.HasOne(d => d.Client).WithMany(p => p.ClientScopes).HasForeignKey(d => d.ClientId);
        });

        modelBuilder.Entity<ClientSecrets>(entity =>
        {
            entity.HasIndex(e => e.ClientId, "IX_ClientSecrets_ClientId");

            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Type).HasMaxLength(250);
            entity.Property(e => e.Value).HasMaxLength(4000);

            entity.HasOne(d => d.Client).WithMany(p => p.ClientSecrets).HasForeignKey(d => d.ClientId);
        });

        modelBuilder.Entity<Clients>(entity =>
        {
            entity.HasIndex(e => e.ClientId, "IX_Clients_ClientId").IsUnique();

            entity.Property(e => e.BackChannelLogoutUri).HasMaxLength(2000);
            entity.Property(e => e.ClientClaimsPrefix).HasMaxLength(200);
            entity.Property(e => e.ClientId).HasMaxLength(200);
            entity.Property(e => e.ClientName).HasMaxLength(200);
            entity.Property(e => e.ClientUri).HasMaxLength(2000);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.FrontChannelLogoutUri).HasMaxLength(2000);
            entity.Property(e => e.LogoUri).HasMaxLength(2000);
            entity.Property(e => e.PairWiseSubjectSalt).HasMaxLength(200);
            entity.Property(e => e.ProtocolType).HasMaxLength(200);
            entity.Property(e => e.UserCodeType).HasMaxLength(100);
        });

        modelBuilder.Entity<ClubReps>(entity =>
        {
            entity.HasKey(e => e.Aid).HasName("PK__ClubReps__C6970A10246FCAF8");

            entity.ToTable("ClubReps", "Clubs");

            entity.Property(e => e.ClubRepUserId).HasMaxLength(450);
            entity.Property(e => e.LebUserId).HasMaxLength(450);
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Club).WithMany(p => p.ClubReps)
                .HasForeignKey(d => d.ClubId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ClubReps__ClubId__611EBF60");

            entity.HasOne(d => d.ClubRepUser).WithMany(p => p.ClubRepsClubRepUser)
                .HasForeignKey(d => d.ClubRepUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ClubReps__ClubRe__6212E399");

            entity.HasOne(d => d.LebUser).WithMany(p => p.ClubRepsLebUser)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK__ClubReps__LebUse__630707D2");
        });

        modelBuilder.Entity<ClubTeams>(entity =>
        {
            entity.HasKey(e => e.ClubTeamId).HasName("PK__ClubTeam__831909DCA3DF632A");

            entity.ToTable("ClubTeams", "Clubs");

            entity.Property(e => e.ClubTeamGradYear).IsUnicode(false);
            entity.Property(e => e.ClubTeamLevelOfPlay)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.ClubTeamName).HasMaxLength(80);
            entity.Property(e => e.LebUserId).HasMaxLength(450);
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Club).WithMany(p => p.ClubTeams)
                .HasForeignKey(d => d.ClubId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ClubTeams__ClubI__28A55C13");

            entity.HasOne(d => d.LebUser).WithMany(p => p.ClubTeams)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK__ClubTeams__LebUs__2999804C");
        });

        modelBuilder.Entity<Clubs>(entity =>
        {
            entity.HasKey(e => e.ClubId).HasName("PK__Clubs__D35058E78E5C155F");

            entity.ToTable("Clubs", "Clubs");

            entity.Property(e => e.ClubName)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.LebUserId).HasMaxLength(450);
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Clubs)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK__Clubs__LebUserId__5D4E2E7C");
        });

        modelBuilder.Entity<Clubs1>(entity =>
        {
            entity.HasKey(e => e.ClubId).HasName("PK_reference.clubs");

            entity.ToTable("clubs", "reference");

            entity.Property(e => e.ClubId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("clubID");
            entity.Property(e => e.ClubName).HasColumnName("clubName");
            entity.Property(e => e.DirectorEmail).HasColumnName("directorEmail");
            entity.Property(e => e.DirectorName).HasColumnName("directorName");
            entity.Property(e => e.DirectorPhone1).HasColumnName("directorPhone1");
            entity.Property(e => e.DirectorPhone2).HasColumnName("directorPhone2");
            entity.Property(e => e.DirectorPhone3).HasColumnName("directorPhone3");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.PresEmail).HasColumnName("presEmail");
            entity.Property(e => e.PresName).HasColumnName("presName");
            entity.Property(e => e.PresPhone1).HasColumnName("presPhone1");
            entity.Property(e => e.PresPhone2).HasColumnName("presPhone2");
            entity.Property(e => e.PresPhone3).HasColumnName("presPhone3");
            entity.Property(e => e.RepEmail).HasColumnName("repEmail");
            entity.Property(e => e.RepName).HasColumnName("repName");
            entity.Property(e => e.RepPhone1).HasColumnName("repPhone1");
            entity.Property(e => e.RepPhone2).HasColumnName("repPhone2");
            entity.Property(e => e.RepPhone3).HasColumnName("repPhone3");
            entity.Property(e => e.SchedulerEmail).HasColumnName("schedulerEmail");
            entity.Property(e => e.SchedulerName).HasColumnName("schedulerName");
            entity.Property(e => e.SchedulerPhone1).HasColumnName("schedulerPhone1");
            entity.Property(e => e.SchedulerPhone2).HasColumnName("schedulerPhone2");
            entity.Property(e => e.SchedulerPhone3).HasColumnName("schedulerPhone3");
            entity.Property(e => e.SportId).HasColumnName("sportID");
            entity.Property(e => e.UrlWebsite).HasColumnName("urlWebsite");
            entity.Property(e => e.UrlWebsiteLogo).HasColumnName("urlWebsiteLogo");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Clubs1)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_reference.clubs_AspNetUsers_lebUserID");

            entity.HasOne(d => d.Sport).WithMany(p => p.Clubs1)
                .HasForeignKey(d => d.SportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_reference.clubs_reference.Sports_sportID");
        });

        modelBuilder.Entity<ContactRelationshipCategories>(entity =>
        {
            entity.HasKey(e => e.RelationshipId).HasName("PK_reference.Contact_Relationship_Categories");

            entity.ToTable("Contact_Relationship_Categories", "reference");

            entity.Property(e => e.RelationshipId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("relationshipID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.Relationship).HasColumnName("relationship");

            entity.HasOne(d => d.LebUser).WithMany(p => p.ContactRelationshipCategories)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_reference.Contact_Relationship_Categories_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<ContactTypeCategories>(entity =>
        {
            entity.HasKey(e => e.ContactTypeId).HasName("PK_reference.Contact_Type_Categories");

            entity.ToTable("Contact_Type_Categories", "reference");

            entity.Property(e => e.ContactTypeId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("contactTypeID");
            entity.Property(e => e.ContactType).HasColumnName("contactType");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.LebUser).WithMany(p => p.ContactTypeCategories)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_reference.Contact_Type_Categories_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<CustomerGroupCustomers>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Customer__3214EC0774B29D91");

            entity.ToTable("CustomerGroupCustomers", "Jobs");

            entity.HasOne(d => d.CustomerGroup).WithMany(p => p.CustomerGroupCustomers)
                .HasForeignKey(d => d.CustomerGroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CustomerG__Custo__1EDBFAB7");

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerGroupCustomers)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CustomerG__Custo__1FD01EF0");
        });

        modelBuilder.Entity<CustomerGroups>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Customer__3214EC072048F2FD");

            entity.ToTable("CustomerGroups", "Jobs");
        });

        modelBuilder.Entity<Customers>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("PK_Jobs.Customers");

            entity.ToTable("Customers", "Jobs");

            entity.Property(e => e.CustomerId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("customerID");
            entity.Property(e => e.AdnLoginId).HasColumnName("adnLoginID");
            entity.Property(e => e.AdnTransactionKey).HasColumnName("adnTransactionKey");
            entity.Property(e => e.CustomerAi)
                .ValueGeneratedOnAdd()
                .HasColumnName("customerAI");
            entity.Property(e => e.CustomerName).HasColumnName("customerName");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.Theme)
                .HasMaxLength(450)
                .HasColumnName("theme");
            entity.Property(e => e.TzId).HasColumnName("TZ_ID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Customers)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.Customers_AspNetUsers_lebUserID");

            entity.HasOne(d => d.ThemeNavigation).WithMany(p => p.Customers)
                .HasForeignKey(d => d.Theme)
                .HasConstraintName("FK_Jobs.Customers_reference.themes_theme");

            entity.HasOne(d => d.Tz).WithMany(p => p.Customers)
                .HasForeignKey(d => d.TzId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Customers_reference.Timezones_TZ_ID");
        });

        modelBuilder.Entity<DeviceCodes>(entity =>
        {
            entity.HasKey(e => e.UserCode);

            entity.HasIndex(e => e.DeviceCode, "IX_DeviceCodes_DeviceCode").IsUnique();

            entity.HasIndex(e => e.UserCode, "IX_DeviceCodes_UserCode").IsUnique();

            entity.Property(e => e.UserCode).HasMaxLength(200);
            entity.Property(e => e.ClientId).HasMaxLength(200);
            entity.Property(e => e.DeviceCode).HasMaxLength(200);
            entity.Property(e => e.SubjectId).HasMaxLength(200);
        });

        modelBuilder.Entity<DeviceGids>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Device_G__3214EC07B8F424A5");

            entity.ToTable("Device_Gids", "mobile");

            entity.Property(e => e.DeviceId).HasMaxLength(450);

            entity.HasOne(d => d.Device).WithMany(p => p.DeviceGids)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Device_Gi__Devic__75A40FA3");

            entity.HasOne(d => d.GidNavigation).WithMany(p => p.DeviceGids)
                .HasForeignKey(d => d.Gid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Device_Gids__Gid__769833DC");
        });

        modelBuilder.Entity<DeviceJobs>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Device_J__3214EC072DA8A2FD");

            entity.ToTable("Device_Jobs", "mobile");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.DeviceId).HasMaxLength(450);
            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");

            entity.HasOne(d => d.Device).WithMany(p => p.DeviceJobs)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Device_Jo__Devic__1645E95F");

            entity.HasOne(d => d.Job).WithMany(p => p.DeviceJobs)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Device_Jo__JobID__173A0D98");
        });

        modelBuilder.Entity<DeviceRegistrationIds>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Device_R__3214EC0749D42C45");

            entity.ToTable("Device_RegistrationIds", "mobile");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Active).HasColumnName("active");
            entity.Property(e => e.DeviceId).HasMaxLength(450);
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.RegistrationId).HasColumnName("RegistrationID");

            entity.HasOne(d => d.Device).WithMany(p => p.DeviceRegistrationIds)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Device_Re__Devic__21429ABF");

            entity.HasOne(d => d.Registration).WithMany(p => p.DeviceRegistrationIds)
                .HasForeignKey(d => d.RegistrationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Device_Re__Regis__2236BEF8");
        });

        modelBuilder.Entity<DeviceTeams>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Device_T__3214EC07192F83A1");

            entity.ToTable("Device_Teams", "mobile");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.DeviceId).HasMaxLength(450);
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.RegistrationId).HasColumnName("RegistrationID");
            entity.Property(e => e.TeamId).HasColumnName("TeamID");

            entity.HasOne(d => d.Device).WithMany(p => p.DeviceTeams)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Device_Te__Devic__4FDD8E17");

            entity.HasOne(d => d.Registration).WithMany(p => p.DeviceTeams)
                .HasForeignKey(d => d.RegistrationId)
                .HasConstraintName("FK__Device_Te__Regis__5D378935");

            entity.HasOne(d => d.Team).WithMany(p => p.DeviceTeams)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Device_Te__TeamI__50D1B250");
        });

        modelBuilder.Entity<Devices>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Devices__3214EC07B036B49B");

            entity.ToTable("Devices", "mobile");

            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.Token).HasMaxLength(450);
            entity.Property(e => e.Type)
                .HasMaxLength(100)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Divisions>(entity =>
        {
            entity.HasKey(e => e.DivId).HasName("PK_Leagues.divisions");

            entity.ToTable("divisions", "Leagues", tb => tb.HasTrigger("Division_AfterEdit_UpdateScheduleDivNames"));

            entity.Property(e => e.DivId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("divID");
            entity.Property(e => e.AgegroupId).HasColumnName("agegroupID");
            entity.Property(e => e.DivName).HasColumnName("divName");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.MaxRoundNumberToShow).HasColumnName("maxRoundNumberToShow");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.Agegroup).WithMany(p => p.Divisions)
                .HasForeignKey(d => d.AgegroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Leagues.divisions_Leagues.agegroups_agegroupID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Divisions)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Leagues.divisions_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<EmailFailures>(entity =>
        {
            entity.HasKey(e => e.EmailFailureId).HasName("PK_Jobs.emailFailures");

            entity.ToTable("emailFailures", "Jobs");

            entity.Property(e => e.EmailFailureId).HasColumnName("emailFailureID");
            entity.Property(e => e.EmailBody).HasColumnName("emailBody");
            entity.Property(e => e.EmailSubject).HasColumnName("emailSubject");
            entity.Property(e => e.FailedEmailAddresses).HasColumnName("failedEmailAddresses");
            entity.Property(e => e.FailureMsg).HasColumnName("failureMsg");
            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.SentTs)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("sentTS");
            entity.Property(e => e.SentbyUserId)
                .HasMaxLength(450)
                .HasColumnName("sentbyUserId");

            entity.HasOne(d => d.Job).WithMany(p => p.EmailFailures)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.emailFailures_Jobs.Jobs_JobID");

            entity.HasOne(d => d.SentbyUser).WithMany(p => p.EmailFailures)
                .HasForeignKey(d => d.SentbyUserId)
                .HasConstraintName("FK_Jobs.emailFailures_AspNetUsers_sentbyUserId");
        });

        modelBuilder.Entity<EmailLast100>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("EmailLast100", "Jobs");

            entity.Property(e => e.Count).HasColumnName("count");
            entity.Property(e => e.CustomerName).HasColumnName("customerName");
            entity.Property(e => e.EmailId).HasColumnName("emailID");
            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.JobName).HasColumnName("jobName");
            entity.Property(e => e.Msg).HasColumnName("msg");
            entity.Property(e => e.SendFrom).HasColumnName("sendFrom");
            entity.Property(e => e.SendTo).HasColumnName("sendTo");
            entity.Property(e => e.SendTs).HasColumnName("sendTS");
            entity.Property(e => e.SenderUserId)
                .HasMaxLength(450)
                .HasColumnName("senderUserID");
            entity.Property(e => e.Subject).HasColumnName("subject");
        });

        modelBuilder.Entity<EmailLogs>(entity =>
        {
            entity.HasKey(e => e.EmailId).HasName("PK_Jobs.emailLogs");

            entity.ToTable("emailLogs", "Jobs");

            entity.Property(e => e.EmailId).HasColumnName("emailID");
            entity.Property(e => e.Count).HasColumnName("count");
            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.Msg).HasColumnName("msg");
            entity.Property(e => e.SendFrom).HasColumnName("sendFrom");
            entity.Property(e => e.SendTo).HasColumnName("sendTo");
            entity.Property(e => e.SendTs)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("sendTS");
            entity.Property(e => e.SenderUserId)
                .HasMaxLength(450)
                .HasColumnName("senderUserID");
            entity.Property(e => e.Subject).HasColumnName("subject");

            entity.HasOne(d => d.Job).WithMany(p => p.EmailLogs)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK_Jobs.emailLogs_Jobs.Jobs_JobID");

            entity.HasOne(d => d.SenderUser).WithMany(p => p.EmailLogs)
                .HasForeignKey(d => d.SenderUserId)
                .HasConstraintName("FK_Jobs.emailLogs_AspNetUsers_senderUserID");
        });

        modelBuilder.Entity<Families>(entity =>
        {
            entity.HasKey(e => e.FamilyUserId).HasName("PK__Families__1788CC4CA5C3D609");

            entity.Property(e => e.FamilyUserId).HasColumnName("Family_UserId");
            entity.Property(e => e.DadCellphone)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("Dad_Cellphone");
            entity.Property(e => e.DadCellphoneProvider)
                .HasMaxLength(450)
                .HasColumnName("Dad_CellphoneProvider");
            entity.Property(e => e.DadEmail)
                .HasMaxLength(256)
                .IsUnicode(false)
                .HasColumnName("Dad_Email");
            entity.Property(e => e.DadFirstName)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("Dad_FirstName");
            entity.Property(e => e.DadLastName)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("Dad_LastName");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())", "DF_Families_modified")
                .HasColumnName("modified");
            entity.Property(e => e.MomCellphone)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("Mom_Cellphone");
            entity.Property(e => e.MomCellphoneProvider)
                .HasMaxLength(450)
                .HasColumnName("Mom_CellphoneProvider");
            entity.Property(e => e.MomEmail)
                .HasMaxLength(256)
                .IsUnicode(false)
                .HasColumnName("Mom_Email");
            entity.Property(e => e.MomFirstName)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("Mom_FirstName");
            entity.Property(e => e.MomLastName)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("Mom_LastName");

            entity.HasOne(d => d.DadCellphoneProviderNavigation).WithMany(p => p.FamiliesDadCellphoneProviderNavigation)
                .HasForeignKey(d => d.DadCellphoneProvider)
                .HasConstraintName("FK__Families__Dad_Ce__53E25DCE");

            entity.HasOne(d => d.FamilyUser).WithOne(p => p.FamiliesFamilyUser)
                .HasForeignKey<Families>(d => d.FamilyUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Families__UserId__51FA155C");

            entity.HasOne(d => d.LebUser).WithMany(p => p.FamiliesLebUser)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Families_AspNetUsers");

            entity.HasOne(d => d.MomCellphoneProviderNavigation).WithMany(p => p.FamiliesMomCellphoneProviderNavigation)
                .HasForeignKey(d => d.MomCellphoneProvider)
                .HasConstraintName("FK__Families__Mom_Ce__52EE3995");
        });

        modelBuilder.Entity<FamilyMembers>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_Family_Members_1");

            entity.ToTable("Family_Members");

            entity.Property(e => e.FamilyMemberUserId)
                .HasMaxLength(450)
                .HasColumnName("Family_Member_UserId");
            entity.Property(e => e.FamilyUserId)
                .HasMaxLength(450)
                .HasColumnName("Family_UserId");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())", "DF_Family_Members_modified")
                .HasColumnName("modified");

            entity.HasOne(d => d.FamilyMemberUser).WithMany(p => p.FamilyMembers)
                .HasForeignKey(d => d.FamilyMemberUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Family_Members_AspNetUsers");

            entity.HasOne(d => d.FamilyUser).WithMany(p => p.FamilyMembers)
                .HasForeignKey(d => d.FamilyUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Family_Members_Families");
        });

        modelBuilder.Entity<FieldOverridesStartTimeMaxMinGames>(entity =>
        {
            entity.HasKey(e => e.Ai).HasName("PK__Field_Ov__3213A922B93A52C8");

            entity.ToTable("Field_Overrides_StartTime_MaxMinGames", "Leagues");

            entity.Property(e => e.Ai).HasColumnName("ai");
            entity.Property(e => e.Dow)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("dow");
            entity.Property(e => e.FieldId).HasColumnName("fieldID");
            entity.Property(e => e.LeagueId).HasColumnName("leagueID");
            entity.Property(e => e.MaxGamesPerField).HasColumnName("maxGamesPerField");
            entity.Property(e => e.MinGamesPerField).HasColumnName("minGamesPerField");
            entity.Property(e => e.Season)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("season");
            entity.Property(e => e.StartTime)
                .HasMaxLength(5)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("startTime");
            entity.Property(e => e.Year)
                .HasMaxLength(4)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("year");

            entity.HasOne(d => d.Field).WithMany(p => p.FieldOverridesStartTimeMaxMinGames)
                .HasForeignKey(d => d.FieldId)
                .HasConstraintName("FK__Field_Ove__field__7DEE718A");

            entity.HasOne(d => d.League).WithMany(p => p.FieldOverridesStartTimeMaxMinGames)
                .HasForeignKey(d => d.LeagueId)
                .HasConstraintName("FK__Field_Ove__leagu__7CFA4D51");
        });

        modelBuilder.Entity<Fields>(entity =>
        {
            entity.HasKey(e => e.FieldId).HasName("PK_reference.Fields");

            entity.ToTable("Fields", "reference", tb => tb.HasTrigger("Field_AfterEdit_UpdateScheduleFieldName"));

            entity.Property(e => e.FieldId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("fieldID");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.Directions).HasColumnName("directions");
            entity.Property(e => e.FName).HasColumnName("fName");
            entity.Property(e => e.Latitude).HasComputedColumnSql("([Location].[Lat])", false);
            entity.Property(e => e.LebUserId).HasColumnName("lebUserID");
            entity.Property(e => e.Location).HasColumnName("location");
            entity.Property(e => e.Longitude).HasComputedColumnSql("([Location].[Long])", false);
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.State).HasColumnName("state");
            entity.Property(e => e.Zip).HasColumnName("zip");
        });

        modelBuilder.Entity<FieldsLeagueSeason>(entity =>
        {
            entity.HasKey(e => e.FlsId).HasName("PK_Leagues.Fields_LeagueSeason");

            entity.ToTable("Fields_LeagueSeason", "Leagues");

            entity.Property(e => e.FlsId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("flsID");
            entity.Property(e => e.BActive)
                .HasDefaultValue(true)
                .HasColumnName("bActive");
            entity.Property(e => e.FieldId).HasColumnName("fieldID");
            entity.Property(e => e.LeagueId).HasColumnName("leagueID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.Season).HasColumnName("season");

            entity.HasOne(d => d.Field).WithMany(p => p.FieldsLeagueSeason)
                .HasForeignKey(d => d.FieldId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Leagues.Fields_LeagueSeason_reference.Fields_fieldID");

            entity.HasOne(d => d.League).WithMany(p => p.FieldsLeagueSeason)
                .HasForeignKey(d => d.LeagueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Leagues.Fields_LeagueSeason_Leagues.leagues_leagueID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.FieldsLeagueSeason)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Leagues.Fields_LeagueSeason_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<Ftexts>(entity =>
        {
            entity.HasKey(e => e.MenuItemId).HasName("PK_Jobs.Ftexts");

            entity.ToTable("Ftexts", "Jobs");

            entity.Property(e => e.MenuItemId)
                .ValueGeneratedNever()
                .HasColumnName("menuItemID");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.CreateDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("createDate");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.Text).HasColumnName("text");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Ftexts)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.Ftexts_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<GameClockParams>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__GameCloc__3214EC07A9080746");

            entity.ToTable("GameClockParams", "Jobs");

            entity.Property(e => e.HalfMinutes).HasColumnType("decimal(4, 1)");
            entity.Property(e => e.HalfTimeMinutes).HasColumnType("decimal(4, 1)");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.PlayoffHalfMinutes)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(4, 1)");
            entity.Property(e => e.PlayoffHalfTimeMinutes)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(4, 1)");
            entity.Property(e => e.PlayoffMinutes)
                .HasDefaultValue(22m)
                .HasColumnType("decimal(4, 1)");
            entity.Property(e => e.QuarterMinutes)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(4, 1)");
            entity.Property(e => e.QuarterTimeMinutes)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(4, 1)");
            entity.Property(e => e.TransitionMinutes).HasColumnType("decimal(4, 1)");
            entity.Property(e => e.UtcoffsetHours).HasColumnName("UTCOffsetHours");

            entity.HasOne(d => d.Job).WithMany(p => p.GameClockParams)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__GameClock__JobId__5E025B93");
        });

        modelBuilder.Entity<GameStatusCodes>(entity =>
        {
            entity.HasKey(e => e.GStatusCode).HasName("PK_Leagues.GameStatusCodes");

            entity.ToTable("GameStatusCodes", "Leagues");

            entity.Property(e => e.GStatusCode).HasColumnName("g_statusCode");
            entity.Property(e => e.GStatusText).HasColumnName("g_statusText");
        });

        modelBuilder.Entity<IdentityClaims>(entity =>
        {
            entity.HasIndex(e => e.IdentityResourceId, "IX_IdentityClaims_IdentityResourceId");

            entity.Property(e => e.Type).HasMaxLength(200);

            entity.HasOne(d => d.IdentityResource).WithMany(p => p.IdentityClaims).HasForeignKey(d => d.IdentityResourceId);
        });

        modelBuilder.Entity<IdentityProperties>(entity =>
        {
            entity.HasIndex(e => e.IdentityResourceId, "IX_IdentityProperties_IdentityResourceId");

            entity.Property(e => e.Key).HasMaxLength(250);
            entity.Property(e => e.Value).HasMaxLength(2000);

            entity.HasOne(d => d.IdentityResource).WithMany(p => p.IdentityProperties).HasForeignKey(d => d.IdentityResourceId);
        });

        modelBuilder.Entity<IdentityResources>(entity =>
        {
            entity.HasIndex(e => e.Name, "IX_IdentityResources_Name").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<IwlcaCapitalCup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("IWLCA_CapitalCup");

            entity.Property(e => e.Active)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ClubName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Name");
            entity.Property(e => e.CoachEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 1");
            entity.Property(e => e.CoachEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 2");
            entity.Property(e => e.CoachName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 1");
            entity.Property(e => e.CoachName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 2");
            entity.Property(e => e.CoachPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 1");
            entity.Property(e => e.CoachPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 2");
            entity.Property(e => e.Complete)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Division)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.EnrolledByEmail)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Email");
            entity.Property(e => e.EnrolledByName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Name");
            entity.Property(e => e.EventGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Event Grad Year");
            entity.Property(e => e.Flags)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Full Name");
            entity.Property(e => e.Gender)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("ID");
            entity.Property(e => e.ManagerEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 1");
            entity.Property(e => e.ManagerEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 2");
            entity.Property(e => e.ManagerName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 1");
            entity.Property(e => e.ManagerName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 2");
            entity.Property(e => e.ManagerPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 1");
            entity.Property(e => e.ManagerPhone2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 2");
            entity.Property(e => e.PreferredDivision)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Preferred Division");
            entity.Property(e => e.ShortName)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Short Name");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Submitted)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TeamGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team Grad Year");
            entity.Property(e => e.TeamId)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team ID");
            entity.Property(e => e.TeamName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Team Name");
        });

        modelBuilder.Entity<IwlcaChampionsCup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("IWLCA_ChampionsCup");

            entity.Property(e => e.Active)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.ClubName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Club Name");
            entity.Property(e => e.CoachEmail1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Email 1");
            entity.Property(e => e.CoachEmail2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Email 2");
            entity.Property(e => e.CoachName1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Name 1");
            entity.Property(e => e.CoachName2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Name 2");
            entity.Property(e => e.CoachPhone1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 1");
            entity.Property(e => e.CoachPhone2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 2");
            entity.Property(e => e.Complete)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.Division)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.EnrolledByEmail)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Email");
            entity.Property(e => e.EnrolledByName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Name");
            entity.Property(e => e.EventGradYear)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Event Grad Year");
            entity.Property(e => e.Flags)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.FullName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Full Name");
            entity.Property(e => e.Gender)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.Id)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("ID");
            entity.Property(e => e.ManagerEmail1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Email 1");
            entity.Property(e => e.ManagerEmail2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Email 2");
            entity.Property(e => e.ManagerName1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Name 1");
            entity.Property(e => e.ManagerName2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Name 2");
            entity.Property(e => e.ManagerPhone1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 1");
            entity.Property(e => e.ManagerPhone2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 2");
            entity.Property(e => e.PreferredDivision)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Preferred Division");
            entity.Property(e => e.ShortName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Short Name");
            entity.Property(e => e.State)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.Submitted)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.TeamGradYear)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Team Grad Year");
            entity.Property(e => e.TeamId)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Team ID");
            entity.Property(e => e.TeamName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Team Name");
        });

        modelBuilder.Entity<IwlcaDebut>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("IWLCA_Debut");

            entity.Property(e => e.Active)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ClubDirectorSEmailAddress)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Email Address");
            entity.Property(e => e.ClubDirectorSName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Name");
            entity.Property(e => e.ClubDirectorSPhoneNumber)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Phone Number");
            entity.Property(e => e.ClubName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Name");
            entity.Property(e => e.ClubTeamSFullMailingAddress)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Team's Full Mailing Address");
            entity.Property(e => e.ClubTeamSLocationState)
                .HasMaxLength(1000)
                .IsUnicode(false)
                .HasColumnName("Club Team's Location  State");
            entity.Property(e => e.CoachEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 1");
            entity.Property(e => e.CoachEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 2");
            entity.Property(e => e.CoachName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 1");
            entity.Property(e => e.CoachName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 2");
            entity.Property(e => e.CoachPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 1");
            entity.Property(e => e.CoachPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 2");
            entity.Property(e => e.Complete)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Division)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.EnrolledByEmail)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Email");
            entity.Property(e => e.EnrolledByName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Name");
            entity.Property(e => e.EventGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Event Grad Year");
            entity.Property(e => e.Flags)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Full Name");
            entity.Property(e => e.Gender)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("ID");
            entity.Property(e => e.ManagerEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 1");
            entity.Property(e => e.ManagerEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 2");
            entity.Property(e => e.ManagerName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 1");
            entity.Property(e => e.ManagerName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 2");
            entity.Property(e => e.ManagerPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 1");
            entity.Property(e => e.ManagerPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 2");
            entity.Property(e => e.PreferredDivision)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Preferred Division");
            entity.Property(e => e.ShortName)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Short Name");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Submitted)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TeamGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team Grad Year");
            entity.Property(e => e.TeamId)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team ID");
            entity.Property(e => e.TeamName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Team Name");
        });

        modelBuilder.Entity<IwlcaNewEngland>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("IWLCA_NewEngland");

            entity.Property(e => e.Active)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ClubName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Name");
            entity.Property(e => e.CoachEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 1");
            entity.Property(e => e.CoachEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 2");
            entity.Property(e => e.CoachName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 1");
            entity.Property(e => e.CoachName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 2");
            entity.Property(e => e.CoachPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 1");
            entity.Property(e => e.CoachPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 2");
            entity.Property(e => e.Complete)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Division)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.EnrolledByEmail)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Email");
            entity.Property(e => e.EnrolledByName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Name");
            entity.Property(e => e.EventGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Event Grad Year");
            entity.Property(e => e.Flags)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Full Name");
            entity.Property(e => e.Gender)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("ID");
            entity.Property(e => e.ManagerEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 1");
            entity.Property(e => e.ManagerEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 2");
            entity.Property(e => e.ManagerName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 1");
            entity.Property(e => e.ManagerName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 2");
            entity.Property(e => e.ManagerPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 1");
            entity.Property(e => e.ManagerPhone2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 2");
            entity.Property(e => e.PreferredDivision)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Preferred Division");
            entity.Property(e => e.ShortName)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Short Name");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Submitted)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TeamGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team Grad Year");
            entity.Property(e => e.TeamId)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team ID");
            entity.Property(e => e.TeamName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Team Name");
        });

        modelBuilder.Entity<IwlcaPresidentsCup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("IWLCA_PresidentsCup");

            entity.Property(e => e.Active)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ClubDirectorSEmailAddress)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Email Address");
            entity.Property(e => e.ClubDirectorSName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Name");
            entity.Property(e => e.ClubDirectorSPhoneNumber)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Phone Number");
            entity.Property(e => e.ClubName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Name");
            entity.Property(e => e.ClubTeamSFullMailingAddress)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Team's Full Mailing Address");
            entity.Property(e => e.ClubTeamSLocationState)
                .HasMaxLength(1000)
                .IsUnicode(false)
                .HasColumnName("Club Team's Location  State");
            entity.Property(e => e.CoachEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 1");
            entity.Property(e => e.CoachEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 2");
            entity.Property(e => e.CoachName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 1");
            entity.Property(e => e.CoachName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 2");
            entity.Property(e => e.CoachPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 1");
            entity.Property(e => e.CoachPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 2");
            entity.Property(e => e.Complete)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Division)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.EnrolledByEmail)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Email");
            entity.Property(e => e.EnrolledByName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Name");
            entity.Property(e => e.EventGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Event Grad Year");
            entity.Property(e => e.Flags)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Full Name");
            entity.Property(e => e.Gender)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("ID");
            entity.Property(e => e.ManagerEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 1");
            entity.Property(e => e.ManagerEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 2");
            entity.Property(e => e.ManagerName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 1");
            entity.Property(e => e.ManagerName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 2");
            entity.Property(e => e.ManagerPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 1");
            entity.Property(e => e.ManagerPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 2");
            entity.Property(e => e.PreferredDivision)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Preferred Division");
            entity.Property(e => e.ShortName)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Short Name");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Submitted)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TeamGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team Grad Year");
            entity.Property(e => e.TeamId)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team ID");
            entity.Property(e => e.TeamName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Team Name");
        });

        modelBuilder.Entity<IwlcaSoutheastCup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("IWLCA_SoutheastCup");

            entity.Property(e => e.AccountPaymentMethod)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Account Payment Method");
            entity.Property(e => e.Active)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.BillingName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Billing Name");
            entity.Property(e => e.ClubName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Club Name");
            entity.Property(e => e.CoachEmail1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Email 1");
            entity.Property(e => e.CoachEmail2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Email 2");
            entity.Property(e => e.CoachName1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Name 1");
            entity.Property(e => e.CoachName2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Name 2");
            entity.Property(e => e.CoachPhone1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 1");
            entity.Property(e => e.CoachPhone2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 2");
            entity.Property(e => e.Column24)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Column 24");
            entity.Property(e => e.Complete)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.Division)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.EnrolledByEmail)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Email");
            entity.Property(e => e.EnrolledByName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Name");
            entity.Property(e => e.EventGradYear)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Event Grad Year");
            entity.Property(e => e.FeeGroup)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Fee Group");
            entity.Property(e => e.Flags)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.FullName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Full Name");
            entity.Property(e => e.Gender)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.Id)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("ID");
            entity.Property(e => e.Invoiced)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.LastPaymentCheckId)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Last Payment Check ID");
            entity.Property(e => e.LastPaymentMethod)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Last Payment Method");
            entity.Property(e => e.Lop)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("LOP");
            entity.Property(e => e.ManagerEmail1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Email 1");
            entity.Property(e => e.ManagerEmail2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Email 2");
            entity.Property(e => e.ManagerName1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Name 1");
            entity.Property(e => e.ManagerName2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Name 2");
            entity.Property(e => e.ManagerPhone1)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 1");
            entity.Property(e => e.ManagerPhone2)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 2");
            entity.Property(e => e.PaymentStatus)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Payment Status");
            entity.Property(e => e.PreferredDivision)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Preferred Division");
            entity.Property(e => e.ShortName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Short Name");
            entity.Property(e => e.State)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.Submitted)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.TeamGradYear)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Team Grad Year");
            entity.Property(e => e.TeamId)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Team ID");
            entity.Property(e => e.TeamName)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Team Name");
        });

        modelBuilder.Entity<IwlcaSouthwestCup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("IWLCA_SouthwestCup");

            entity.Property(e => e.Active)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ClubDirectorSEmailAddress)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Email Address");
            entity.Property(e => e.ClubDirectorSName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Name");
            entity.Property(e => e.ClubDirectorSPhoneNumber)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Phone Number");
            entity.Property(e => e.ClubName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Name");
            entity.Property(e => e.ClubTeamSFullMailingAddress)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Team's Full Mailing Address");
            entity.Property(e => e.ClubTeamSLocationState)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Club Team's Location  State");
            entity.Property(e => e.CoachEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 1");
            entity.Property(e => e.CoachEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 2");
            entity.Property(e => e.CoachName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 1");
            entity.Property(e => e.CoachName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 2");
            entity.Property(e => e.CoachPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 1");
            entity.Property(e => e.CoachPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 2");
            entity.Property(e => e.Complete)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Division)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.EnrolledByEmail)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Email");
            entity.Property(e => e.EnrolledByName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Name");
            entity.Property(e => e.EventGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Event Grad Year");
            entity.Property(e => e.Flags)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Full Name");
            entity.Property(e => e.Gender)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("ID");
            entity.Property(e => e.ManagerEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 1");
            entity.Property(e => e.ManagerEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 2");
            entity.Property(e => e.ManagerName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 1");
            entity.Property(e => e.ManagerName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 2");
            entity.Property(e => e.ManagerPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 1");
            entity.Property(e => e.ManagerPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 2");
            entity.Property(e => e.PreferredDivision)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Preferred Division");
            entity.Property(e => e.ShortName)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Short Name");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Submitted)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TeamGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team Grad Year");
            entity.Property(e => e.TeamId)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team ID");
            entity.Property(e => e.TeamName)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team Name");
        });

        modelBuilder.Entity<IwlcaWestCoastCup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("IWLCA_WestCoastCup");

            entity.Property(e => e.Active)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ClubDirectorSEmailAddress)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Email Address");
            entity.Property(e => e.ClubDirectorSName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Name");
            entity.Property(e => e.ClubDirectorSPhoneNumber)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Director's Phone Number");
            entity.Property(e => e.ClubName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Name");
            entity.Property(e => e.ClubTeamSFullMailingAddress)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Team's Full Mailing Address");
            entity.Property(e => e.ClubTeamSLocationState)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Club Team's Location  State");
            entity.Property(e => e.CoachEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 1");
            entity.Property(e => e.CoachEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Email 2");
            entity.Property(e => e.CoachName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 1");
            entity.Property(e => e.CoachName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Name 2");
            entity.Property(e => e.CoachPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 1");
            entity.Property(e => e.CoachPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coach Phone 2");
            entity.Property(e => e.Complete)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Division)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.EnrolledByEmail)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Email");
            entity.Property(e => e.EnrolledByName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Enrolled By Name");
            entity.Property(e => e.EventGradYear)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Event Grad Year");
            entity.Property(e => e.Flags)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Full Name");
            entity.Property(e => e.Gender)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("ID");
            entity.Property(e => e.ManagerEmail1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 1");
            entity.Property(e => e.ManagerEmail2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Email 2");
            entity.Property(e => e.ManagerName1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 1");
            entity.Property(e => e.ManagerName2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Name 2");
            entity.Property(e => e.ManagerPhone1)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 1");
            entity.Property(e => e.ManagerPhone2)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Manager Phone 2");
            entity.Property(e => e.PleaseSelectYourDesiredFlight)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Please select your desired flight");
            entity.Property(e => e.PreferredDivision)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Preferred Division");
            entity.Property(e => e.ShortName)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Short Name");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Submitted)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TeamGradYear)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team Grad Year");
            entity.Property(e => e.TeamId)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Team ID");
            entity.Property(e => e.TeamName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Team Name");
        });

        modelBuilder.Entity<JobAdminChargeTypes>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__JobAdmin__3214EC07A60E651F");

            entity.ToTable("JobAdminChargeTypes", "reference");

            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<JobAdminCharges>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__JobAdmin__3214EC075EF0CD38");

            entity.ToTable("JobAdminCharges", "adn");

            entity.Property(e => e.ChargeAmount).HasColumnType("money");
            entity.Property(e => e.Comment).IsUnicode(false);
            entity.Property(e => e.CreateDate).HasColumnType("datetime");

            entity.HasOne(d => d.ChargeType).WithMany(p => p.JobAdminCharges)
                .HasForeignKey(d => d.ChargeTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__JobAdminC__Charg__1181FF99");

            entity.HasOne(d => d.Job).WithMany(p => p.JobAdminCharges)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__JobAdminC__JobId__127623D2");
        });

        modelBuilder.Entity<JobAgeRanges>(entity =>
        {
            entity.HasKey(e => e.AgeRangeId).HasName("PK_Jobs.JobAgeRanges");

            entity.ToTable("JobAgeRanges", "Jobs");

            entity.Property(e => e.AgeRangeId).HasColumnName("AgeRangeID");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.RangeLeft).HasColumnName("rangeLeft");
            entity.Property(e => e.RangeName).HasColumnName("rangeName");
            entity.Property(e => e.RangeRight).HasColumnName("rangeRight");

            entity.HasOne(d => d.Job).WithMany(p => p.JobAgeRanges)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.JobAgeRanges_Jobs.Jobs_jobID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobAgeRanges)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.JobAgeRanges_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<JobCalendar>(entity =>
        {
            entity.HasKey(e => e.CalendarEventId).HasName("PK__JobCalen__87A58A52C5366DE6");

            entity.ToTable("JobCalendar", "Jobs");

            entity.Property(e => e.CalendarEventColor)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.CalendarEventEnd).HasColumnType("datetime");
            entity.Property(e => e.CalendarEventStart).HasColumnType("datetime");
            entity.Property(e => e.CalendarEventTextColor)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.CalendarEventTitle)
                .HasMaxLength(80)
                .IsUnicode(false);

            entity.HasOne(d => d.Job).WithMany(p => p.JobCalendar)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__JobCalend__JobId__2F518E4B");
        });

        modelBuilder.Entity<JobControllers>(entity =>
        {
            entity.HasKey(e => e.Controller).HasName("PK_Jobs.Job_Controllers");

            entity.ToTable("Job_Controllers", "Jobs");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobControllers)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.Job_Controllers_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<JobCustomers>(entity =>
        {
            entity.HasKey(e => e.JobCustomerId).HasName("PK_Jobs.Job_Customers");

            entity.ToTable("Job_Customers", "Jobs");

            entity.Property(e => e.JobCustomerId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("JobCustomerID");
            entity.Property(e => e.CustomerId).HasColumnName("customerID");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.Customer).WithMany(p => p.JobCustomers)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Job_Customers_Jobs.Customers_customerID");

            entity.HasOne(d => d.Job).WithMany(p => p.JobCustomers)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Job_Customers_Jobs.Jobs_jobID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobCustomers)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.Job_Customers_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<JobDirectorsRegistrationTs>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("JobDirectorsRegistrationTS", "utility");

            entity.Property(e => e.RegistrationTs).HasColumnName("RegistrationTS");
            entity.Property(e => e.RoleId).HasMaxLength(450);
        });

        modelBuilder.Entity<JobDiscountCodes>(entity =>
        {
            entity.HasKey(e => e.Ai).HasName("PK__Job_Disc__3213A922AF84DAFC");

            entity.ToTable("Job_DiscountCodes", "Jobs");

            entity.Property(e => e.Ai).HasColumnName("ai");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.BAsPercent)
                .HasDefaultValue(true)
                .HasColumnName("bAsPercent");
            entity.Property(e => e.CodeAmount)
                .HasColumnType("money")
                .HasColumnName("codeAmount");
            entity.Property(e => e.CodeEndDate)
                .HasColumnType("datetime")
                .HasColumnName("codeEndDate");
            entity.Property(e => e.CodeName)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("codeName");
            entity.Property(e => e.CodeStartDate)
                .HasColumnType("datetime")
                .HasColumnName("codeStartDate");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");

            entity.HasOne(d => d.Job).WithMany(p => p.JobDiscountCodes)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Job_Disco__jobID__1DE63FD0");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobDiscountCodes)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Job_Disco__lebUs__20C2AC7B");
        });

        modelBuilder.Entity<JobDisplayOptions>(entity =>
        {
            entity.HasKey(e => e.JobId);

            entity.ToTable("JobDisplayOptions", "Jobs");

            entity.Property(e => e.JobId)
                .HasDefaultValueSql("(newsequentialid())", "DF_JobDisplayOptions_jobID")
                .HasColumnName("jobID");
            entity.Property(e => e.BlockPurchase)
                .IsUnicode(false)
                .HasColumnName("Block_Purchase");
            entity.Property(e => e.BlockRecentImage1)
                .IsUnicode(false)
                .HasColumnName("Block_Recent_Image1");
            entity.Property(e => e.BlockRecentImage2)
                .IsUnicode(false)
                .HasColumnName("Block_Recent_Image2");
            entity.Property(e => e.BlockRecentImage3)
                .IsUnicode(false)
                .HasColumnName("Block_Recent_Image3");
            entity.Property(e => e.BlockRecentImage4)
                .IsUnicode(false)
                .HasColumnName("Block_Recent_Image4");
            entity.Property(e => e.BlockRecentWorks)
                .IsUnicode(false)
                .HasColumnName("Block_RecentWorks");
            entity.Property(e => e.BlockService)
                .IsUnicode(false)
                .HasColumnName("Block_Service");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.LogoFooter)
                .IsUnicode(false)
                .HasColumnName("logo_footer");
            entity.Property(e => e.LogoHeader)
                .IsUnicode(false)
                .HasColumnName("logo_header");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())", "DF_JobDisplayOptions_modified")
                .HasColumnName("modified");
            entity.Property(e => e.ParallaxBackgroundImage)
                .IsUnicode(false)
                .HasColumnName("parallaxBackgroundImage");
            entity.Property(e => e.ParallaxSlide1Image)
                .IsUnicode(false)
                .HasColumnName("parallaxSlide1Image");
            entity.Property(e => e.ParallaxSlide1Text1)
                .IsUnicode(false)
                .HasColumnName("parallaxSlide1Text1");
            entity.Property(e => e.ParallaxSlide1Text2)
                .IsUnicode(false)
                .HasColumnName("parallaxSlide1Text2");
            entity.Property(e => e.ParallaxSlide2Image)
                .IsUnicode(false)
                .HasColumnName("parallaxSlide2Image");
            entity.Property(e => e.ParallaxSlide2Text1)
                .IsUnicode(false)
                .HasColumnName("parallaxSlide2Text1");
            entity.Property(e => e.ParallaxSlide2Text2)
                .IsUnicode(false)
                .HasColumnName("parallaxSlide2Text2");
            entity.Property(e => e.ParallaxSlide3Image)
                .IsUnicode(false)
                .HasColumnName("parallaxSlide3Image");
            entity.Property(e => e.ParallaxSlide3Text1)
                .IsUnicode(false)
                .HasColumnName("parallaxSlide3Text1");
            entity.Property(e => e.ParallaxSlide3Text2)
                .IsUnicode(false)
                .HasColumnName("parallaxSlide3Text2");
            entity.Property(e => e.ParallaxSlideCount).HasColumnName("parallaxSlideCount");

            entity.HasOne(d => d.Job).WithOne(p => p.JobDisplayOptions)
                .HasForeignKey<JobDisplayOptions>(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_JobDisplayOptions_Jobs");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobDisplayOptions)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_JobDisplayOptions_AspNetUsers");
        });

        modelBuilder.Entity<JobInvoiceNumbers>(entity =>
        {
            entity.HasKey(e => e.InvoiceNumberId).HasName("PK_Jobs.Job_InvoiceNumbers");

            entity.ToTable("Job_InvoiceNumbers", "Jobs");

            entity.Property(e => e.InvoiceNumberId).HasColumnName("InvoiceNumberID");
            entity.Property(e => e.CustomerAi).HasColumnName("CustomerAI");
            entity.Property(e => e.InvoiceNumber).HasColumnName("Invoice_Number");
            entity.Property(e => e.JobAi).HasColumnName("JobAI");
        });

        modelBuilder.Entity<JobLeagues>(entity =>
        {
            entity.HasKey(e => e.JobLeagueId).HasName("PK_Jobs.Job_Leagues");

            entity.ToTable("Job_Leagues", "Jobs");

            entity.Property(e => e.JobLeagueId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("JobLeagueID");
            entity.Property(e => e.BIsPrimary).HasColumnName("bIsPrimary");
            entity.Property(e => e.BaseFee)
                .HasColumnType("money")
                .HasColumnName("baseFee");
            entity.Property(e => e.DiscountFee)
                .HasColumnType("money")
                .HasColumnName("discountFee");
            entity.Property(e => e.DiscountFeeEnd).HasColumnName("discountFeeEnd");
            entity.Property(e => e.DiscountFeeStart).HasColumnName("discountFeeStart");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.LateFee)
                .HasColumnType("money")
                .HasColumnName("lateFee");
            entity.Property(e => e.LateFeeEnd).HasColumnName("lateFeeEnd");
            entity.Property(e => e.LateFeeStart).HasColumnName("lateFeeStart");
            entity.Property(e => e.LeagueId).HasColumnName("leagueID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.Job).WithMany(p => p.JobLeagues)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Job_Leagues_Jobs.Jobs_jobID");

            entity.HasOne(d => d.League).WithMany(p => p.JobLeagues)
                .HasForeignKey(d => d.LeagueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Job_Leagues_Leagues.leagues_leagueID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobLeagues)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.Job_Leagues_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<JobMenuItems>(entity =>
        {
            entity.HasKey(e => e.MenuItemId).HasName("PK_Jobs.JobMenu_Items");

            entity.ToTable("JobMenu_Items", "Jobs");

            entity.Property(e => e.MenuItemId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("menuItemID");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.BCollapsed)
                .HasDefaultValue(true)
                .HasColumnName("bCollapsed");
            entity.Property(e => e.BTextWrap)
                .HasDefaultValue(true)
                .HasColumnName("bTextWrap");
            entity.Property(e => e.Controller).HasMaxLength(450);
            entity.Property(e => e.IconName)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("iconName");
            entity.Property(e => e.Index).HasColumnName("index");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.MenuId).HasColumnName("menuID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.ParentMenuItemId).HasColumnName("parentMenuItemID");
            entity.Property(e => e.ReportExportTypeId).HasColumnName("ReportExportTypeID");
            entity.Property(e => e.ReportName)
                .HasMaxLength(450)
                .HasColumnName("reportName");
            entity.Property(e => e.RouterLink)
                .IsUnicode(false)
                .HasColumnName("routerLink");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobMenuItems)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.JobMenu_Items_AspNetUsers_lebUserID");

            entity.HasOne(d => d.Menu).WithMany(p => p.JobMenuItems)
                .HasForeignKey(d => d.MenuId)
                .HasConstraintName("FK_Jobs.JobMenu_Items_Jobs.JobMenus_menuID");

            entity.HasOne(d => d.ReportExportType).WithMany(p => p.JobMenuItems)
                .HasForeignKey(d => d.ReportExportTypeId)
                .HasConstraintName("FK_Jobs.JobMenu_Items_reference.ReportExportTypes_ReportExportTypeID");

            entity.HasOne(d => d.ReportNameNavigation).WithMany(p => p.JobMenuItems)
                .HasForeignKey(d => d.ReportName)
                .HasConstraintName("FK_Jobs.JobMenu_Items_reference.Reports_reportName");
        });

        modelBuilder.Entity<JobMenus>(entity =>
        {
            entity.HasKey(e => e.MenuId).HasName("PK_Jobs.JobMenus");

            entity.ToTable("JobMenus", "Jobs");

            entity.Property(e => e.MenuId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("menuID");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.MenuTypeId).HasColumnName("menuTypeID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.RoleId)
                .HasMaxLength(450)
                .HasColumnName("RoleID");
            entity.Property(e => e.Tag).HasColumnName("tag");

            entity.HasOne(d => d.Job).WithMany(p => p.JobMenus)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.JobMenus_Jobs.Jobs_jobID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobMenus)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.JobMenus_AspNetUsers_lebUserID");

            entity.HasOne(d => d.MenuType).WithMany(p => p.JobMenus)
                .HasForeignKey(d => d.MenuTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.JobMenus_reference.MenuTypes_menuTypeID");

            entity.HasOne(d => d.Role).WithMany(p => p.JobMenus)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("FK_Jobs.JobMenus_AspNetRoles_RoleID");
        });

        modelBuilder.Entity<JobMessages>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__JobMessa__3214EC07F71D6810");

            entity.ToTable("JobMessages", "mobile");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.AttachmentUrl)
                .IsUnicode(false)
                .HasColumnName("attachmentUrl");
            entity.Property(e => e.Content).IsUnicode(false);
            entity.Property(e => e.Createdate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdate");
            entity.Property(e => e.DaysVisible)
                .HasDefaultValue(7)
                .HasColumnName("daysVisible");
            entity.Property(e => e.JobId).HasColumnName("jobId");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.PhotoUrl).IsUnicode(false);
            entity.Property(e => e.RoleId).HasMaxLength(450);
            entity.Property(e => e.Title)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.Job).WithMany(p => p.JobMessages)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK__JobMessag__jobId__1EDB2F60");

            entity.HasOne(d => d.Role).WithMany(p => p.JobMessages)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("FK__JobMessag__RoleI__2864999A");

            entity.HasOne(d => d.SenderRegistration).WithMany(p => p.JobMessages)
                .HasForeignKey(d => d.SenderRegistrationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__JobMessag__Sende__1B0A9E7C");
        });

        modelBuilder.Entity<JobOwlImages>(entity =>
        {
            entity.HasKey(e => e.JobId);

            entity.ToTable("JobOwlImages", "Jobs");

            entity.Property(e => e.JobId)
                .HasDefaultValueSql("(newsequentialid())", "DF_JobOwlImages_jobID")
                .HasColumnName("jobID");
            entity.Property(e => e.Caption)
                .IsUnicode(false)
                .HasColumnName("caption");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())", "DF_JobOwlImages_modified")
                .HasColumnName("modified");
            entity.Property(e => e.OwlImage01)
                .IsUnicode(false)
                .HasColumnName("owlImage01");
            entity.Property(e => e.OwlImage02)
                .IsUnicode(false)
                .HasColumnName("owlImage02");
            entity.Property(e => e.OwlImage03)
                .IsUnicode(false)
                .HasColumnName("owlImage03");
            entity.Property(e => e.OwlImage04)
                .IsUnicode(false)
                .HasColumnName("owlImage04");
            entity.Property(e => e.OwlImage05)
                .IsUnicode(false)
                .HasColumnName("owlImage05");
            entity.Property(e => e.OwlImage06)
                .IsUnicode(false)
                .HasColumnName("owlImage06");
            entity.Property(e => e.OwlImage07)
                .IsUnicode(false)
                .HasColumnName("owlImage07");
            entity.Property(e => e.OwlImage08)
                .IsUnicode(false)
                .HasColumnName("owlImage08");
            entity.Property(e => e.OwlImage09)
                .IsUnicode(false)
                .HasColumnName("owlImage09");
            entity.Property(e => e.OwlImage10)
                .IsUnicode(false)
                .HasColumnName("owlImage10");
            entity.Property(e => e.OwlSlideCount).HasColumnName("owlSlideCount");

            entity.HasOne(d => d.Job).WithOne(p => p.JobOwlImages)
                .HasForeignKey<JobOwlImages>(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_JobOwlImages_Jobs");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobOwlImages)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_JobOwlImages_AspNetUsers");
        });

        modelBuilder.Entity<JobPushNotificationsToAll>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Job_Push__3214EC07A693B05D");

            entity.ToTable("Job_PushNotifications_ToAll", "mobile");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.PushText).IsUnicode(false);

            entity.HasOne(d => d.Job).WithMany(p => p.JobPushNotificationsToAll)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Job_PushN__JobID__55EC387F");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobPushNotificationsToAll)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Job_PushN__lebUs__56E05CB8");

            entity.HasOne(d => d.Team).WithMany(p => p.JobPushNotificationsToAll)
                .HasForeignKey(d => d.TeamId)
                .HasConstraintName("FK_PushNotifications_TeamId");
        });

        modelBuilder.Entity<JobReportExportHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__JobRepor__3214EC079EDEF083");

            entity.ToTable("JobReportExportHistory", "Jobs");

            entity.Property(e => e.ExportDate).HasColumnType("datetime");
            entity.Property(e => e.ReportName).IsUnicode(false);
            entity.Property(e => e.StoredProcedureName).IsUnicode(false);

            entity.HasOne(d => d.Registration).WithMany(p => p.JobReportExportHistory)
                .HasForeignKey(d => d.RegistrationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__JobReport__Regis__0A8AE947");
        });

        modelBuilder.Entity<JobSmsbroadcasts>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Job_SMSB__3214EC072318B94E");

            entity.ToTable("Job_SMSBroadcasts", "Jobs");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.Smsbody).HasColumnName("SMSBody");

            entity.HasOne(d => d.Job).WithMany(p => p.JobSmsbroadcasts)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Job_SMSBr__JobID__65A37D5B");

            entity.HasOne(d => d.LebUser).WithMany(p => p.JobSmsbroadcasts)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Job_SMSBr__lebUs__6697A194");
        });

        modelBuilder.Entity<JobTypes>(entity =>
        {
            entity.HasKey(e => e.JobTypeId).HasName("PK_reference.JobTypes");

            entity.ToTable("JobTypes", "reference");

            entity.Property(e => e.JobTypeId).HasColumnName("JobTypeID");
        });

        modelBuilder.Entity<JobWidget>(entity =>
        {
            entity.HasKey(e => e.JobWidgetId).HasName("PK_widgets_JobWidget");

            entity.ToTable("JobWidget", "widgets");

            entity.HasIndex(e => new { e.JobId, e.WidgetId, e.RoleId }, "UQ_widgets_JobWidget_Job_Widget_Role").IsUnique();

            entity.Property(e => e.IsEnabled).HasDefaultValue(true);

            entity.HasOne(d => d.Category).WithMany(p => p.JobWidget)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_widgets_JobWidget_CategoryId");

            entity.HasOne(d => d.Job).WithMany(p => p.JobWidget)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_widgets_JobWidget_JobId");

            entity.HasOne(d => d.Role).WithMany(p => p.JobWidget)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_widgets_JobWidget_RoleId");

            entity.HasOne(d => d.Widget).WithMany(p => p.JobWidget)
                .HasForeignKey(d => d.WidgetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_widgets_JobWidget_WidgetId");
        });

        modelBuilder.Entity<Jobinvoices>(entity =>
        {
            entity.HasKey(e => e.Ai)
                .HasName("PK__jobinvoi__3213A922D2B677DD")
                .HasFillFactor(60);

            entity.ToTable("jobinvoices", "adn");

            entity.Property(e => e.Ai).HasColumnName("ai");
            entity.Property(e => e.Active)
                .HasDefaultValue(false)
                .HasColumnName("active");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.Month).HasColumnName("month");
            entity.Property(e => e.Year).HasColumnName("year");

            entity.HasOne(d => d.Job).WithMany(p => p.Jobinvoices)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK__jobinvoic__jobID__75193467");
        });

        modelBuilder.Entity<Jobs>(entity =>
        {
            entity.HasKey(e => e.JobId).HasName("PK_Jobs.Jobs");

            entity.ToTable("Jobs", "Jobs", tb => tb.HasTrigger("Job_AfterEdit_TeamFees"));

            entity.HasIndex(e => new { e.ExpiryUsers, e.BSuspendPublic, e.BScheduleAllowPublicAccess }, "IX_Jobs_ExpiryUsers_BSuspendPublic_BScheduleAllowPublicAccess").IsDescending(true, false, true);

            entity.HasIndex(e => e.JobPath, "UI_JOBPATH").IsUnique();

            entity.HasIndex(e => e.JobCode, "ui_jobcode_valued").HasFilter("([jobCode] IS NOT NULL)");

            entity.Property(e => e.JobId)
                .HasDefaultValueSql("(newsequentialid())", "DF__Jobs__jobID__2C3E80C8")
                .HasColumnName("jobID");
            entity.Property(e => e.AdnArb)
                .HasDefaultValue(false)
                .HasColumnName("adnARB");
            entity.Property(e => e.AdnArbMinimunTotalCharge).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.AdnArbbillingOccurences).HasColumnName("adnARBBillingOccurences");
            entity.Property(e => e.AdnArbintervalLength)
                .HasDefaultValue(1)
                .HasColumnName("adnARBIntervalLength");
            entity.Property(e => e.AdnArbstartDate)
                .HasColumnType("datetime")
                .HasColumnName("adnARBStartDate");
            entity.Property(e => e.AdultRegCodeOfConduct).HasColumnName("AdultReg_CodeOfConduct");
            entity.Property(e => e.AdultRegConfirmationEmail).HasColumnName("AdultReg_ConfirmationEmail");
            entity.Property(e => e.AdultRegConfirmationOnScreen).HasColumnName("AdultReg_ConfirmationOnScreen");
            entity.Property(e => e.AdultRegRefundPolicy).HasColumnName("AdultReg_RefundPolicy");
            entity.Property(e => e.AdultRegReleaseOfLiability).HasColumnName("AdultReg_ReleaseOfLiability");
            entity.Property(e => e.Alwayscopyemaillist)
                .IsUnicode(false)
                .HasColumnName("alwayscopyemaillist");
            entity.Property(e => e.BAddProcessingFees).HasColumnName("bAddProcessingFees");
            entity.Property(e => e.BAllowCreditAll)
                .HasDefaultValue(false)
                .HasColumnName("bAllowCreditAll");
            entity.Property(e => e.BAllowMobileLogin).HasColumnName("bAllowMobileLogin");
            entity.Property(e => e.BAllowMobileRegn)
                .HasDefaultValue(false)
                .HasColumnName("bAllowMobileRegn");
            entity.Property(e => e.BAllowRefundsInPriorMonths)
                .HasDefaultValue(false)
                .HasColumnName("bAllowRefundsInPriorMonths");
            entity.Property(e => e.BAllowRosterViewAdult).HasColumnName("bAllowRosterViewAdult");
            entity.Property(e => e.BAllowRosterViewPlayer).HasColumnName("bAllowRosterViewPlayer");
            entity.Property(e => e.BApplyProcessingFeesToTeamDeposit)
                .HasDefaultValue(false)
                .HasColumnName("bApplyProcessingFeesToTeamDeposit");
            entity.Property(e => e.BBannerIsCustom).HasColumnName("bBannerIsCustom");
            entity.Property(e => e.BClubRepAllowAdd)
                .HasDefaultValue(false)
                .HasColumnName("bClubRepAllowAdd");
            entity.Property(e => e.BClubRepAllowDelete)
                .HasDefaultValue(false)
                .HasColumnName("bClubRepAllowDelete");
            entity.Property(e => e.BClubRepAllowEdit)
                .HasDefaultValue(false)
                .HasColumnName("bClubRepAllowEdit");
            entity.Property(e => e.BDisallowCcplayerConfirmations).HasColumnName("bDisallowCCPlayerConfirmations");
            entity.Property(e => e.BEnableMobileRsvp)
                .HasDefaultValue(false)
                .HasColumnName("bEnableMobileRsvp");
            entity.Property(e => e.BEnableMobileTeamChat)
                .HasDefaultValue(false)
                .HasColumnName("bEnableMobileTeamChat");
            entity.Property(e => e.BEnableStore)
                .HasDefaultValue(false)
                .HasColumnName("bEnableStore");
            entity.Property(e => e.BEnableTsicteams)
                .HasDefaultValue(false)
                .HasColumnName("bEnableTSICTeams");
            entity.Property(e => e.BOfferPlayerRegsaverInsurance)
                .HasDefaultValue(false)
                .HasColumnName("bOfferPlayerRegsaverInsurance");
            entity.Property(e => e.BOfferTeamRegsaverInsurance).HasColumnName("bOfferTeamRegsaverInsurance");
            entity.Property(e => e.BRegistrationAllowPlayer)
                .HasDefaultValue(false)
                .HasColumnName("bRegistrationAllowPlayer");
            entity.Property(e => e.BRegistrationAllowTeam)
                .HasDefaultValue(false)
                .HasColumnName("bRegistrationAllowTeam");
            entity.Property(e => e.BRestrictPlayerTeamsToAgerange)
                .HasDefaultValue(false)
                .HasColumnName("bRestrictPlayerTeamsToAgerange");
            entity.Property(e => e.BScheduleAllowPublicAccess)
                .HasDefaultValue(false)
                .HasColumnName("bScheduleAllowPublicAccess");
            entity.Property(e => e.BShowTeamNameOnlyInSchedules).HasColumnName("bShowTeamNameOnlyInSchedules");
            entity.Property(e => e.BSignalRschedule)
                .HasDefaultValue(false)
                .HasColumnName("bSignalRSchedule");
            entity.Property(e => e.BSuspendPublic).HasColumnName("bSuspendPublic");
            entity.Property(e => e.BTeamPushDirectors).HasColumnName("bTeamPushDirectors");
            entity.Property(e => e.BTeamsFullPaymentRequired)
                .HasDefaultValue(false)
                .HasColumnName("bTeamsFullPaymentRequired");
            entity.Property(e => e.BUseWaitlists).HasColumnName("bUseWaitlists");
            entity.Property(e => e.Balancedueaspercent).HasColumnName("balancedueaspercent");
            entity.Property(e => e.BannerFile).HasColumnName("bannerFile");
            entity.Property(e => e.BenableStp).HasColumnName("BEnableSTP");
            entity.Property(e => e.BillingTypeId).HasColumnName("BillingTypeID");
            entity.Property(e => e.CoreRegformPlayer).IsUnicode(false);
            entity.Property(e => e.CustomerId).HasColumnName("customerID");
            entity.Property(e => e.DadLabel).IsUnicode(false);
            entity.Property(e => e.DisplayName).IsUnicode(false);
            entity.Property(e => e.EventEndDate).HasColumnType("datetime");
            entity.Property(e => e.EventStartDate).HasColumnType("datetime");
            entity.Property(e => e.ExpiryAdmin).HasDefaultValueSql("(getdate())", "DF__Jobs__ExpiryAdmi__2962141D");
            entity.Property(e => e.ExpiryUsers).HasDefaultValueSql("(getdate())", "DF__Jobs__ExpiryUser__2A563856");
            entity.Property(e => e.JobAi)
                .ValueGeneratedOnAdd()
                .HasColumnName("jobAI");
            entity.Property(e => e.JobCode)
                .HasMaxLength(6)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("jobCode");
            entity.Property(e => e.JobDescription).HasColumnName("jobDescription");
            entity.Property(e => e.JobName).HasColumnName("jobName");
            entity.Property(e => e.JobNameQbp).HasColumnName("jobName_QBP");
            entity.Property(e => e.JobPath)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("jobPath");
            entity.Property(e => e.JobTagline).HasColumnName("jobTagline");
            entity.Property(e => e.JobTypeId).HasColumnName("JobTypeID");
            entity.Property(e => e.JsonOptions).IsUnicode(false);
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.MailTo).HasColumnName("mailTo");
            entity.Property(e => e.MailinPaymentWarning).HasColumnName("mailinPaymentWarning");
            entity.Property(e => e.MobileJobName)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("mobileJobName");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())", "DF__Jobs__modified__2D32A501")
                .HasColumnName("modified");
            entity.Property(e => e.MomLabel).IsUnicode(false);
            entity.Property(e => e.PayTo).HasColumnName("payTo");
            entity.Property(e => e.PaymentMethodsAllowedCode).HasColumnName("PaymentMethodsAllowed_Code");
            entity.Property(e => e.PerMonthCharge)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("perMonthCharge");
            entity.Property(e => e.PerPlayerCharge)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("perPlayerCharge");
            entity.Property(e => e.PerSalesPercentCharge).HasColumnName("perSalesPercentCharge");
            entity.Property(e => e.PerTeamCharge)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("perTeamCharge");
            entity.Property(e => e.PlayerRegCodeOfConduct).HasColumnName("PlayerReg_CodeOfConduct");
            entity.Property(e => e.PlayerRegConfirmationEmail).HasColumnName("PlayerReg_ConfirmationEmail");
            entity.Property(e => e.PlayerRegConfirmationOnScreen).HasColumnName("PlayerReg_ConfirmationOnScreen");
            entity.Property(e => e.PlayerRegCovid19Waiver).HasColumnName("PlayerReg_Covid19Waiver");
            entity.Property(e => e.PlayerRegMultiPlayerDiscountMin).HasColumnName("PlayerReg_MultiPlayerDiscount_Min");
            entity.Property(e => e.PlayerRegMultiPlayerDiscountPercent).HasColumnName("PlayerReg_MultiPlayerDiscount_Percent");
            entity.Property(e => e.PlayerRegRefundPolicy).HasColumnName("PlayerReg_RefundPolicy");
            entity.Property(e => e.PlayerRegReleaseOfLiability).HasColumnName("PlayerReg_ReleaseOfLiability");
            entity.Property(e => e.ProcessingFeePercent).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.RecruiterRegConfirmationEmail).HasColumnName("RecruiterReg_ConfirmationEmail");
            entity.Property(e => e.RecruiterRegConfirmationOnScreen).HasColumnName("RecruiterReg_ConfirmationOnScreen");
            entity.Property(e => e.RefereeRegConfirmationEmail).HasColumnName("RefereeReg_ConfirmationEmail");
            entity.Property(e => e.RefereeRegConfirmationOnScreen).HasColumnName("RefereeReg_ConfirmationOnScreen");
            entity.Property(e => e.RegFormBccs).HasColumnName("RegForm_bccs");
            entity.Property(e => e.RegFormCcs).HasColumnName("RegForm_ccs");
            entity.Property(e => e.RegFormFrom).HasColumnName("RegForm_from");
            entity.Property(e => e.RegformNameClubRep)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Default_Form", "DF_Jobs_RegformName_ClubRep")
                .HasColumnName("RegformName_ClubRep");
            entity.Property(e => e.RegformNameCoach)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Default_Form", "DF_Jobs_RegformName_Coach")
                .HasColumnName("RegformName_Coach");
            entity.Property(e => e.RegformNamePlayer)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Default_Form", "DF_Jobs_RegformName_Player")
                .HasColumnName("RegformName_Player");
            entity.Property(e => e.RegformNameTeam)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Default_Form", "DF_Jobs_RegformName_Team")
                .HasColumnName("RegformName_Team");
            entity.Property(e => e.Rescheduleemaillist)
                .IsUnicode(false)
                .HasColumnName("rescheduleemaillist");
            entity.Property(e => e.SearchenginKeywords).HasColumnName("searchenginKeywords");
            entity.Property(e => e.SearchengineDescription).HasColumnName("searchengineDescription");
            entity.Property(e => e.Season).HasColumnName("season");
            entity.Property(e => e.SportId).HasColumnName("sportID");
            entity.Property(e => e.StoreContactEmail)
                .IsUnicode(false)
                .HasColumnName("storeContactEmail");
            entity.Property(e => e.StorePickupDetails)
                .IsUnicode(false)
                .HasColumnName("storePickupDetails");
            entity.Property(e => e.StoreRefundPolicy)
                .IsUnicode(false)
                .HasColumnName("storeRefundPolicy");
            entity.Property(e => e.StoreSalesTax)
                .HasColumnType("money")
                .HasColumnName("storeSalesTax");
            entity.Property(e => e.StoreTsicrate)
                .HasColumnType("decimal(8, 3)")
                .HasColumnName("storeTSICRate");
            entity.Property(e => e.UslaxNumberValidThroughDate)
                .HasColumnType("datetime")
                .HasColumnName("USLaxNumberValidThroughDate");
            entity.Property(e => e.Year).HasColumnName("year");

            entity.HasOne(d => d.BillingType).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.BillingTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Jobs_reference.Billing_Types_BillingTypeID");

            entity.HasOne(d => d.Customer).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Jobs_Jobs.Customers_customerID");

            entity.HasOne(d => d.JobType).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.JobTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Jobs_reference.JobTypes_JobTypeID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.Jobs_AspNetUsers_lebUserID");

            entity.HasOne(d => d.Sport).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.SportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Jobs_reference.Sports_sportID");
        });

        modelBuilder.Entity<JobsToPurgeRemainingJobIds>(entity =>
        {
            entity.HasKey(e => e.JobId).HasName("PK__JobsToPu__164AA1A89F365EAA");

            entity.Property(e => e.JobId)
                .ValueGeneratedNever()
                .HasColumnName("jobId");
        });

        modelBuilder.Entity<LeagueAgeGroupGameDayInfo>(entity =>
        {
            entity.HasKey(e => e.Ai).HasName("PK__LeagueAg__3213A92296884F25");

            entity.ToTable("LeagueAgeGroupGameDayInfo", "Leagues");

            entity.Property(e => e.Ai).HasColumnName("ai");
            entity.Property(e => e.AgegroupId).HasColumnName("agegroupID");
            entity.Property(e => e.BActive).HasColumnName("bActive");
            entity.Property(e => e.GDay)
                .HasColumnType("datetime")
                .HasColumnName("gDay");
            entity.Property(e => e.GamestartInterval).HasColumnName("gamestartInterval");
            entity.Property(e => e.LeagueId).HasColumnName("leagueID");
            entity.Property(e => e.MaxGamesPerField).HasColumnName("maxGamesPerField");
            entity.Property(e => e.MinGamesPerField).HasColumnName("minGamesPerField");
            entity.Property(e => e.Season)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("season");
            entity.Property(e => e.Stage).HasColumnName("stage");
            entity.Property(e => e.StartTime)
                .HasMaxLength(5)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("startTime");
            entity.Property(e => e.Year)
                .HasMaxLength(4)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("year");

            entity.HasOne(d => d.Agegroup).WithMany(p => p.LeagueAgeGroupGameDayInfo)
                .HasForeignKey(d => d.AgegroupId)
                .HasConstraintName("FK__LeagueAge__agegr__774173FB");

            entity.HasOne(d => d.League).WithMany(p => p.LeagueAgeGroupGameDayInfo)
                .HasForeignKey(d => d.LeagueId)
                .HasConstraintName("FK__LeagueAge__leagu__764D4FC2");
        });

        modelBuilder.Entity<Leagues>(entity =>
        {
            entity.HasKey(e => e.LeagueId).HasName("PK_Leagues.leagues");

            entity.ToTable("leagues", "Leagues");

            entity.Property(e => e.LeagueId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("leagueID");
            entity.Property(e => e.BAllowCoachScoreEntry).HasColumnName("bAllowCoachScoreEntry");
            entity.Property(e => e.BHideContacts).HasColumnName("bHideContacts");
            entity.Property(e => e.BHideStandings).HasColumnName("bHideStandings");
            entity.Property(e => e.BShowScheduleToTeamMembers)
                .HasDefaultValue(true)
                .HasColumnName("bShowScheduleToTeamMembers");
            entity.Property(e => e.BTakeAttendance).HasColumnName("bTakeAttendance");
            entity.Property(e => e.BTrackPenaltyMinutes).HasColumnName("bTrackPenaltyMinutes");
            entity.Property(e => e.BTrackSportsmanshipScores).HasColumnName("bTrackSportsmanshipScores");
            entity.Property(e => e.LeagueName).HasColumnName("leagueName");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.PlayerFeeOverride).HasColumnType("money");
            entity.Property(e => e.RescheduleEmailsToAddon).HasColumnName("rescheduleEmailsToAddon");
            entity.Property(e => e.SportId).HasColumnName("sportID");
            entity.Property(e => e.StrGradYears).HasColumnName("strGradYears");
            entity.Property(e => e.StrLop).HasColumnName("strLOP");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Leagues)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Leagues.leagues_AspNetUsers_lebUserID");

            entity.HasOne(d => d.Sport).WithMany(p => p.Leagues)
                .HasForeignKey(d => d.SportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Leagues.leagues_reference.Sports_sportID");

            entity.HasOne(d => d.StandingsSortProfile).WithMany(p => p.Leagues)
                .HasForeignKey(d => d.StandingsSortProfileId)
                .HasConstraintName("FK__leagues__Standin__7CFBE3FF");
        });

        modelBuilder.Entity<Masterpairingtable>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__masterpa__3214EC0795BABEFE");

            entity.ToTable("masterpairingtable", "reference");

            entity.Property(e => e.GCnt).HasColumnName("gCnt");
            entity.Property(e => e.GNo).HasColumnName("gNo");
            entity.Property(e => e.Rnd).HasColumnName("rnd");
            entity.Property(e => e.T1).HasColumnName("t1");
            entity.Property(e => e.T2).HasColumnName("t2");
            entity.Property(e => e.TCnt).HasColumnName("tCnt");
        });

        modelBuilder.Entity<MenuItems>(entity =>
        {
            entity.HasKey(e => e.MenuItemId).HasName("PK__Menu_Ite__8943F7224ED4E14A");

            entity.ToTable("Menu_Items", "Menus");

            entity.Property(e => e.MenuItemId).ValueGeneratedNever();
            entity.Property(e => e.IconName).IsUnicode(false);
            entity.Property(e => e.ModuleName).IsUnicode(false);
            entity.Property(e => e.RouteName).IsUnicode(false);

            entity.HasOne(d => d.Menu).WithMany(p => p.MenuItems)
                .HasForeignKey(d => d.MenuId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Menu_Item__MenuI__7F6D70FF");

            entity.HasOne(d => d.ParentMenuItem).WithMany(p => p.InverseParentMenuItem)
                .HasForeignKey(d => d.ParentMenuItemId)
                .HasConstraintName("FK__Menu_Item__Paren__00619538");
        });

        modelBuilder.Entity<MenuTypes>(entity =>
        {
            entity.HasKey(e => e.MenuTypeId).HasName("PK_reference.MenuTypes");

            entity.ToTable("MenuTypes", "reference");

            entity.Property(e => e.MenuTypeId).HasColumnName("menuTypeID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.MenuType).HasColumnName("menuType");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.LebUser).WithMany(p => p.MenuTypes)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_reference.MenuTypes_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<Menus>(entity =>
        {
            entity.HasKey(e => e.MenuId).HasName("PK__Menus__C99ED2303852FECE");

            entity.ToTable("Menus", "Menus");

            entity.Property(e => e.MenuId).ValueGeneratedNever();
            entity.Property(e => e.RoleId).HasMaxLength(450);

            entity.HasOne(d => d.Job).WithMany(p => p.Menus)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Menus__JobId__7E794CC6");

            entity.HasOne(d => d.Role).WithMany(p => p.Menus)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("FK__Menus__RoleId__7D85288D");
        });

        modelBuilder.Entity<MigrationHistoryOld>(entity =>
        {
            entity.HasKey(e => new { e.MigrationId, e.ContextKey }).HasName("PK_MigrationHistory");

            entity.ToTable("__MigrationHistory_Old");

            entity.Property(e => e.MigrationId).HasMaxLength(150);
            entity.Property(e => e.ContextKey).HasMaxLength(300);
            entity.Property(e => e.ProductVersion).HasMaxLength(32);
        });

        modelBuilder.Entity<MobileUserData>(entity =>
        {
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.User).WithMany(p => p.MobileUserData).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<MonthlyJobStats>(entity =>
        {
            entity.HasKey(e => e.Aid)
                .HasName("PK__Monthly___DE508E2E2F518E4B")
                .HasFillFactor(60);

            entity.ToTable("Monthly_Job_Stats", "adn");

            entity.Property(e => e.Aid).HasColumnName("aid");
            entity.Property(e => e.CountActivePlayersToDate).HasColumnName("Count_ActivePlayersToDate");
            entity.Property(e => e.CountActivePlayersToDateLastMonth).HasColumnName("Count_ActivePlayersToDate_LastMonth");
            entity.Property(e => e.CountActiveTeamsToDate).HasColumnName("Count_ActiveTeamsToDate");
            entity.Property(e => e.CountActiveTeamsToDateLastMonth).HasColumnName("Count_ActiveTeamsToDate_LastMonth");
            entity.Property(e => e.CountNewPlayersThisMonth).HasColumnName("Count_NewPlayers_ThisMonth");
            entity.Property(e => e.CountNewTeamsThisMonth).HasColumnName("Count_NewTeams_ThisMonth");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())", "DF__Monthly_J__modif__33221F2F")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.Month).HasColumnName("month");
            entity.Property(e => e.Year).HasColumnName("year");

            entity.HasOne(d => d.Job).WithMany(p => p.MonthlyJobStats)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Monthly_Job_Stats_Jobs");

            entity.HasOne(d => d.LebUser).WithMany(p => p.MonthlyJobStats)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Monthly_J__lebUs__322DFAF6");
        });

        modelBuilder.Entity<NuveiBatches>(entity =>
        {
            entity.HasKey(e => e.Ai).HasName("PK__NuveiBat__3213A922124ABC61");

            entity.ToTable("NuveiBatches", "adn");

            entity.Property(e => e.Ai).HasColumnName("ai");
            entity.Property(e => e.BatchNet).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ReturnAmt).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.SaleAmt).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<NuveiFunding>(entity =>
        {
            entity.HasKey(e => e.Ai).HasName("PK__NuveiFun__3213A92215F7BDB9");

            entity.ToTable("NuveiFunding", "adn");

            entity.Property(e => e.Ai).HasColumnName("ai");
            entity.Property(e => e.FundingAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.FundingEvent).HasMaxLength(50);
            entity.Property(e => e.FundingType).HasMaxLength(50);
            entity.Property(e => e.RefNumber).HasMaxLength(50);
        });

        modelBuilder.Entity<OpenIddictApplications>(entity =>
        {
            entity.Property(e => e.ClientId).HasMaxLength(450);
        });

        modelBuilder.Entity<OpenIddictTokens>(entity =>
        {
            entity.Property(e => e.ApplicationId).HasMaxLength(450);
            entity.Property(e => e.AuthorizationId).HasMaxLength(450);

            entity.HasOne(d => d.Application).WithMany(p => p.OpenIddictTokens).HasForeignKey(d => d.ApplicationId);

            entity.HasOne(d => d.Authorization).WithMany(p => p.OpenIddictTokens).HasForeignKey(d => d.AuthorizationId);
        });

        modelBuilder.Entity<PairingsLeagueSeason>(entity =>
        {
            entity.HasKey(e => e.Ai).HasName("PK__Pairings__3213A922E0E47CAD");

            entity.ToTable("Pairings_LeagueSeason", "Leagues");

            entity.Property(e => e.Ai).HasColumnName("ai");
            entity.Property(e => e.GCnt).HasColumnName("gCnt");
            entity.Property(e => e.GameNumber).HasColumnName("game_number");
            entity.Property(e => e.LeagueId).HasColumnName("leagueID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.Rnd).HasColumnName("rnd");
            entity.Property(e => e.Season)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("season");
            entity.Property(e => e.T1Annotation)
                .IsUnicode(false)
                .HasColumnName("T1_Annotation");
            entity.Property(e => e.T1CalcType)
                .IsUnicode(false)
                .HasColumnName("T1_CalcType");
            entity.Property(e => e.T1GnoRef).HasColumnName("T1_GNoRef");
            entity.Property(e => e.T1Type)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("T1_Type");
            entity.Property(e => e.T2Annotation)
                .IsUnicode(false)
                .HasColumnName("T2_Annotation");
            entity.Property(e => e.T2CalcType)
                .IsUnicode(false)
                .HasColumnName("T2_CalcType");
            entity.Property(e => e.T2GnoRef).HasColumnName("T2_GNoRef");
            entity.Property(e => e.T2Type)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("T2_Type");
            entity.Property(e => e.TCnt).HasColumnName("tCnt");

            entity.HasOne(d => d.LebUser).WithMany(p => p.PairingsLeagueSeason)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Pairings___lebUs__38CF4036");

            entity.HasOne(d => d.T1TypeNavigation).WithMany(p => p.PairingsLeagueSeasonT1TypeNavigation)
                .HasForeignKey(d => d.T1Type)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Pairings___T1_Ty__36E6F7C4");

            entity.HasOne(d => d.T2TypeNavigation).WithMany(p => p.PairingsLeagueSeasonT2TypeNavigation)
                .HasForeignKey(d => d.T2Type)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Pairings___T2_Ty__37DB1BFD");
        });

        modelBuilder.Entity<PersistedGrants>(entity =>
        {
            entity.HasKey(e => e.Key);

            entity.HasIndex(e => new { e.SubjectId, e.ClientId, e.Type }, "IX_PersistedGrants_SubjectId_ClientId_Type");

            entity.Property(e => e.Key).HasMaxLength(200);
            entity.Property(e => e.ClientId).HasMaxLength(200);
            entity.Property(e => e.SubjectId).HasMaxLength(200);
            entity.Property(e => e.Type).HasMaxLength(50);
        });

        modelBuilder.Entity<PersonContacts>(entity =>
        {
            entity.HasKey(e => e.UserId);

            entity.ToTable("Person_Contacts");

            entity.Property(e => e.CcB1).HasColumnName("ccB1");
            entity.Property(e => e.CcB2).HasColumnName("ccB2");
            entity.Property(e => e.CcB3).HasColumnName("ccB3");
            entity.Property(e => e.CcB4).HasColumnName("ccB4");
            entity.Property(e => e.CcCellphone).HasColumnName("ccCellphone");
            entity.Property(e => e.CcCellphoneProvider).HasColumnName("ccCellphoneProvider");
            entity.Property(e => e.CcFirstName).HasColumnName("ccFirstName");
            entity.Property(e => e.CcLastName).HasColumnName("ccLastName");
            entity.Property(e => e.CeB1).HasColumnName("ceB1");
            entity.Property(e => e.CeB2).HasColumnName("ceB2");
            entity.Property(e => e.CeB3).HasColumnName("ceB3");
            entity.Property(e => e.CeB4).HasColumnName("ceB4");
            entity.Property(e => e.CeCellphone).HasColumnName("ceCellphone");
            entity.Property(e => e.CeCellphoneProvider)
                .HasMaxLength(450)
                .HasColumnName("ceCellphoneProvider");
            entity.Property(e => e.CeEmail).HasColumnName("ceEmail");
            entity.Property(e => e.CeEmailSms).HasColumnName("ceEmailSMS");
            entity.Property(e => e.CeFirstName).HasColumnName("ceFirstName");
            entity.Property(e => e.CeHomephone).HasColumnName("ceHomephone");
            entity.Property(e => e.CeLastName).HasColumnName("ceLastName");
            entity.Property(e => e.CeRelationshipId).HasColumnName("ceRelationshipID");
            entity.Property(e => e.CeWorkphone).HasColumnName("ceWorkphone");
            entity.Property(e => e.CpB1).HasColumnName("cpB1");
            entity.Property(e => e.CpB2).HasColumnName("cpB2");
            entity.Property(e => e.CpB3).HasColumnName("cpB3");
            entity.Property(e => e.CpB4).HasColumnName("cpB4");
            entity.Property(e => e.CpCellphone).HasColumnName("cpCellphone");
            entity.Property(e => e.CpCellphoneProvider)
                .HasMaxLength(450)
                .HasColumnName("cpCellphoneProvider");
            entity.Property(e => e.CpEmail).HasColumnName("cpEmail");
            entity.Property(e => e.CpEmailSms).HasColumnName("cpEmailSMS");
            entity.Property(e => e.CpFirstName).HasColumnName("cpFirstName");
            entity.Property(e => e.CpHomephone).HasColumnName("cpHomephone");
            entity.Property(e => e.CpLastName).HasColumnName("cpLastName");
            entity.Property(e => e.CpRelationshipId).HasColumnName("cpRelationshipID");
            entity.Property(e => e.CpWorkphone).HasColumnName("cpWorkphone");
            entity.Property(e => e.CsB1).HasColumnName("csB1");
            entity.Property(e => e.CsB2).HasColumnName("csB2");
            entity.Property(e => e.CsB3).HasColumnName("csB3");
            entity.Property(e => e.CsB4).HasColumnName("csB4");
            entity.Property(e => e.CsCellphone).HasColumnName("csCellphone");
            entity.Property(e => e.CsCellphoneProvider)
                .HasMaxLength(450)
                .HasColumnName("csCellphoneProvider");
            entity.Property(e => e.CsEmail).HasColumnName("csEmail");
            entity.Property(e => e.CsEmailSms).HasColumnName("csEmailSMS");
            entity.Property(e => e.CsFirstName).HasColumnName("csFirstName");
            entity.Property(e => e.CsHomephone).HasColumnName("csHomephone");
            entity.Property(e => e.CsLastName).HasColumnName("csLastName");
            entity.Property(e => e.CsRelationshipId).HasColumnName("csRelationshipID");
            entity.Property(e => e.CsWorkphone).HasColumnName("csWorkphone");
            entity.Property(e => e.LebUserId).HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.REmail).HasColumnName("rEmail");
            entity.Property(e => e.REmailSms).HasColumnName("rEmailSMS");

            entity.HasOne(d => d.CeCellphoneProviderNavigation).WithMany(p => p.PersonContactsCeCellphoneProviderNavigation)
                .HasForeignKey(d => d.CeCellphoneProvider)
                .HasConstraintName("FK_Person_Contacts_reference.cellphonecarrier_domains_ceCellphoneProvider");

            entity.HasOne(d => d.CeRelationship).WithMany(p => p.PersonContactsCeRelationship)
                .HasForeignKey(d => d.CeRelationshipId)
                .HasConstraintName("FK_Person_Contacts_reference.Contact_Relationship_Categories_ceRelationshipID");

            entity.HasOne(d => d.CpCellphoneProviderNavigation).WithMany(p => p.PersonContactsCpCellphoneProviderNavigation)
                .HasForeignKey(d => d.CpCellphoneProvider)
                .HasConstraintName("FK_Person_Contacts_reference.cellphonecarrier_domains_cpCellphoneProvider");

            entity.HasOne(d => d.CpRelationship).WithMany(p => p.PersonContactsCpRelationship)
                .HasForeignKey(d => d.CpRelationshipId)
                .HasConstraintName("FK_Person_Contacts_reference.Contact_Relationship_Categories_cpRelationshipID");

            entity.HasOne(d => d.CsCellphoneProviderNavigation).WithMany(p => p.PersonContactsCsCellphoneProviderNavigation)
                .HasForeignKey(d => d.CsCellphoneProvider)
                .HasConstraintName("FK_Person_Contacts_reference.cellphonecarrier_domains_csCellphoneProvider");

            entity.HasOne(d => d.CsRelationship).WithMany(p => p.PersonContactsCsRelationship)
                .HasForeignKey(d => d.CsRelationshipId)
                .HasConstraintName("FK_Person_Contacts_reference.Contact_Relationship_Categories_csRelationshipID");

            entity.HasOne(d => d.User).WithOne(p => p.PersonContacts)
                .HasForeignKey<PersonContacts>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<PushNotifications>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__PushNoti__3214EC07C614F4FD");

            entity.ToTable("PushNotifications", "Pushnotifications");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Body).IsUnicode(false);
            entity.Property(e => e.Created)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.JobLogoUrl).IsUnicode(false);
            entity.Property(e => e.JobName).IsUnicode(false);
            entity.Property(e => e.QpAgegroupId).HasColumnName("qp_AgegroupId");
            entity.Property(e => e.QpDivId).HasColumnName("qp_DivId");
            entity.Property(e => e.QpJobId).HasColumnName("qp_JobId");
            entity.Property(e => e.QpLeagueId).HasColumnName("qp_LeagueId");
            entity.Property(e => e.QpRegId).HasColumnName("qp_RegId");
            entity.Property(e => e.QpRole)
                .IsUnicode(false)
                .HasColumnName("qp_Role");
            entity.Property(e => e.QpTeamId).HasColumnName("qp_TeamId");
            entity.Property(e => e.Title).IsUnicode(false);

            entity.HasOne(d => d.AuthorRegistration).WithMany(p => p.PushNotificationsAuthorRegistration)
                .HasForeignKey(d => d.AuthorRegistrationId)
                .HasConstraintName("FK__PushNotif__Autho__47FD4084");

            entity.HasOne(d => d.QpAgegroup).WithMany(p => p.PushNotifications)
                .HasForeignKey(d => d.QpAgegroupId)
                .HasConstraintName("FK__PushNotif__qp_Ag__43388B67");

            entity.HasOne(d => d.QpDiv).WithMany(p => p.PushNotifications)
                .HasForeignKey(d => d.QpDivId)
                .HasConstraintName("FK__PushNotif__qp_Di__442CAFA0");

            entity.HasOne(d => d.QpJob).WithMany(p => p.PushNotifications)
                .HasForeignKey(d => d.QpJobId)
                .HasConstraintName("FK__PushNotif__qp_Jo__415042F5");

            entity.HasOne(d => d.QpLeague).WithMany(p => p.PushNotifications)
                .HasForeignKey(d => d.QpLeagueId)
                .HasConstraintName("FK__PushNotif__qp_Le__4244672E");

            entity.HasOne(d => d.QpReg).WithMany(p => p.PushNotificationsQpReg)
                .HasForeignKey(d => d.QpRegId)
                .HasConstraintName("FK__PushNotif__qp_Re__4614F812");

            entity.HasOne(d => d.QpTeam).WithMany(p => p.PushNotifications)
                .HasForeignKey(d => d.QpTeamId)
                .HasConstraintName("FK__PushNotif__qp_Te__4520D3D9");
        });

        modelBuilder.Entity<PushSubscriptionJobs>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__PushSubs__3214EC07F265BAB1");

            entity.ToTable("PushSubscriptionJobs", "Pushnotifications");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Created)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Job).WithMany(p => p.PushSubscriptionJobs)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PushSubsc__JobId__3C8B8DD8");

            entity.HasOne(d => d.Subscription).WithMany(p => p.PushSubscriptionJobs)
                .HasForeignKey(d => d.SubscriptionId)
                .HasConstraintName("FK__PushSubsc__Subsc__3B97699F");
        });

        modelBuilder.Entity<PushSubscriptionRegistrations>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__PushSubs__3214EC07B25AE531");

            entity.ToTable("PushSubscriptionRegistrations", "Pushnotifications");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Created)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Registration).WithMany(p => p.PushSubscriptionRegistrations)
                .HasForeignKey(d => d.RegistrationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PushSubsc__Regis__3119DB2C");

            entity.HasOne(d => d.Subscription).WithMany(p => p.PushSubscriptionRegistrations)
                .HasForeignKey(d => d.SubscriptionId)
                .HasConstraintName("FK__PushSubsc__Subsc__3025B6F3");
        });

        modelBuilder.Entity<PushSubscriptionTeams>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__PushSubs__3214EC07C0E3F395");

            entity.ToTable("PushSubscriptionTeams", "Pushnotifications");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Created)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Subscription).WithMany(p => p.PushSubscriptionTeams)
                .HasForeignKey(d => d.SubscriptionId)
                .HasConstraintName("FK__PushSubsc__Subsc__35DE9049");

            entity.HasOne(d => d.Team).WithMany(p => p.PushSubscriptionTeams)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PushSubsc__TeamI__36D2B482");
        });

        modelBuilder.Entity<PushSubscriptions>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__PushSubs__3214EC0774772B2D");

            entity.ToTable("PushSubscriptions", "Pushnotifications");

            entity.HasIndex(e => e.Endpoint, "ix_Endpoint").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Auth).IsUnicode(false);
            entity.Property(e => e.Created)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Endpoint)
                .HasMaxLength(1000)
                .IsUnicode(false);
            entity.Property(e => e.P256dh)
                .IsUnicode(false)
                .HasColumnName("P256DH");
        });

        modelBuilder.Entity<RefGameAssigments>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__RefGameA__3214EC07C048465D");

            entity.ToTable("RefGameAssigments", "Leagues");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Game).WithMany(p => p.RefGameAssigments)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RefGameAs__GameI__1102DCAC");

            entity.HasOne(d => d.LebUser).WithMany(p => p.RefGameAssigments)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RefGameAs__lebUs__12EB251E");

            entity.HasOne(d => d.RefRegistration).WithMany(p => p.RefGameAssigments)
                .HasForeignKey(d => d.RefRegistrationId)
                .HasConstraintName("FK__RefGameAs__RefRe__100EB873");
        });

        modelBuilder.Entity<RegFormFieldOptions>(entity =>
        {
            entity.HasKey(e => e.RegFormFieldOptionId).HasName("PK__RegFormF__5B83DA27CB6DAF45");

            entity.ToTable("RegFormField_Options", "forms");

            entity.Property(e => e.RegFormFieldOptionId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.OptionText).IsUnicode(false);
            entity.Property(e => e.OptionValue).IsUnicode(false);

            entity.HasOne(d => d.RegFormField).WithMany(p => p.RegFormFieldOptions)
                .HasForeignKey(d => d.RegFormFieldId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RegFormFi__RegFo__04D1449A");
        });

        modelBuilder.Entity<RegFormFieldTypes>(entity =>
        {
            entity.HasKey(e => e.RegFormFieldTypeId).HasName("PK__RegFormF__48DE8E66F7A5E7F4");

            entity.ToTable("RegFormFieldTypes", "forms");

            entity.Property(e => e.RegFormFieldTypeId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.RegFormFieldType)
                .HasMaxLength(80)
                .IsUnicode(false);
        });

        modelBuilder.Entity<RegFormFields>(entity =>
        {
            entity.HasKey(e => e.RegFormFieldId).HasName("PK__RegFormF__39B4D7D20745246B");

            entity.ToTable("RegFormFields", "forms");

            entity.Property(e => e.RegFormFieldId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.BadminOnly).HasColumnName("BAdminOnly");
            entity.Property(e => e.BremoteValidation).HasColumnName("BRemoteValidation");
            entity.Property(e => e.FieldHint).IsUnicode(false);
            entity.Property(e => e.FieldLabel)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.FieldName)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.ValidatorIsRequiredErrorMessage).IsUnicode(false);
            entity.Property(e => e.ValidatorMustBeTrueErrorMessage).IsUnicode(false);
            entity.Property(e => e.ValidatorRangeErrorMessage).IsUnicode(false);
            entity.Property(e => e.ValidatorRangeMax).HasColumnType("decimal(18, 0)");
            entity.Property(e => e.ValidatorRangeMin).HasColumnType("decimal(18, 0)");
            entity.Property(e => e.ValidatorRegEx).IsUnicode(false);
            entity.Property(e => e.ValidatorRegExErrorMessage).IsUnicode(false);

            entity.HasOne(d => d.RegFormFieldType).WithMany(p => p.RegFormFields)
                .HasForeignKey(d => d.RegFormFieldTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RegFormFi__RegFo__03DD2061");

            entity.HasOne(d => d.RegForm).WithMany(p => p.RegFormFields)
                .HasForeignKey(d => d.RegFormId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RegFormFi__RegFo__02E8FC28");
        });

        modelBuilder.Entity<RegForms>(entity =>
        {
            entity.HasKey(e => e.RegFormId).HasName("PK__RegForms__03C8B5DA4A0C0A0A");

            entity.ToTable("RegForms", "forms");

            entity.Property(e => e.RegFormId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.AllowPif).HasColumnName("AllowPIF");
            entity.Property(e => e.BallowMultipleRegistrations).HasColumnName("BAllowMultipleRegistrations");
            entity.Property(e => e.FormName)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.RoleIdRegistering).HasMaxLength(450);

            entity.HasOne(d => d.Job).WithMany(p => p.RegForms)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RegForms__JobId__0100B3B6");

            entity.HasOne(d => d.RoleIdRegisteringNavigation).WithMany(p => p.RegForms)
                .HasForeignKey(d => d.RoleIdRegistering)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RegForms__RoleId__01F4D7EF");
        });

        modelBuilder.Entity<RegistrationAccounting>(entity =>
        {
            entity.HasKey(e => e.AId).HasName("PK_Jobs.Registration_Accounting");

            entity.ToTable("Registration_Accounting", "Jobs");

            entity.Property(e => e.AId).HasColumnName("aID");
            entity.Property(e => e.Active).HasColumnName("active");
            entity.Property(e => e.AdnCc4).HasColumnName("adnCC4");
            entity.Property(e => e.AdnCcexpDate).HasColumnName("adnCCExpDate");
            entity.Property(e => e.AdnInvoiceNo).HasColumnName("adnInvoiceNo");
            entity.Property(e => e.AdnTransactionId).HasColumnName("adnTransactionID");
            entity.Property(e => e.CheckNo).HasColumnName("checkNo");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Createdate).HasColumnName("createdate");
            entity.Property(e => e.Dueamt)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("dueamt");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.Payamt)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("payamt");
            entity.Property(e => e.PaymentMethodId).HasColumnName("paymentMethodID");
            entity.Property(e => e.Paymeth).HasColumnName("paymeth");
            entity.Property(e => e.PromoCode).HasColumnName("promoCode");
            entity.Property(e => e.RegistrationId).HasColumnName("RegistrationID");
            entity.Property(e => e.TeamId).HasColumnName("teamID");

            entity.HasOne(d => d.DiscountCodeAiNavigation).WithMany(p => p.RegistrationAccounting)
                .HasForeignKey(d => d.DiscountCodeAi)
                .HasConstraintName("FK__Registrat__Disco__116138B1");

            entity.HasOne(d => d.LebUser).WithMany(p => p.RegistrationAccounting)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.Registration_Accounting_AspNetUsers_lebUserID");

            entity.HasOne(d => d.PaymentMethod).WithMany(p => p.RegistrationAccounting)
                .HasForeignKey(d => d.PaymentMethodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Registration_Accounting_reference.Accounting_PaymentMethods_paymentMethodID");

            entity.HasOne(d => d.Registration).WithMany(p => p.RegistrationAccounting)
                .HasForeignKey(d => d.RegistrationId)
                .HasConstraintName("FK_Jobs.Registration_Accounting_Jobs.Registrations_RegistrationID");

            entity.HasOne(d => d.Team).WithMany(p => p.RegistrationAccounting)
                .HasForeignKey(d => d.TeamId)
                .HasConstraintName("FK_Jobs.Registration_Accounting_Leagues.teams_teamID");
        });

        modelBuilder.Entity<RegistrationFormPaymentMethods>(entity =>
        {
            entity.HasKey(e => e.RegistrationFormPaymentMethodId).HasName("PK_reference.Registration_Form_Payment_Methods");

            entity.ToTable("Registration_Form_Payment_Methods", "reference");

            entity.Property(e => e.RegistrationFormPaymentMethodId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("registrationFormPaymentMethodID");
            entity.Property(e => e.RegistrationFormPaymentMethod).HasColumnName("registrationFormPaymentMethod");
        });

        modelBuilder.Entity<Registrations>(entity =>
        {
            entity.HasKey(e => e.RegistrationId).HasName("PK_Jobs.Registrations");

            entity.ToTable("Registrations", "Jobs", tb => tb.HasTrigger("UpdateRegistrantAssignment"));

            entity.HasIndex(e => e.RegistrationAi, "UI_Registrations_Ai").IsUnique();

            entity.Property(e => e.RegistrationId)
                .HasDefaultValueSql("(newsequentialid())", "DF__Registrat__Regis__3C3FDE67")
                .HasColumnName("RegistrationID");
            entity.Property(e => e.Act).HasColumnName("act");
            entity.Property(e => e.AdnSubscriptionAmountPerOccurence)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("adnSubscriptionAmountPerOccurence");
            entity.Property(e => e.AdnSubscriptionBillingOccurences).HasColumnName("adnSubscriptionBillingOccurences");
            entity.Property(e => e.AdnSubscriptionId)
                .IsUnicode(false)
                .HasColumnName("adnSubscriptionId");
            entity.Property(e => e.AdnSubscriptionIntervalLength)
                .HasDefaultValue(1)
                .HasColumnName("adnSubscriptionIntervalLength");
            entity.Property(e => e.AdnSubscriptionStartDate)
                .HasColumnType("datetime")
                .HasColumnName("adnSubscriptionStartDate");
            entity.Property(e => e.AdnSubscriptionStatus)
                .IsUnicode(false)
                .HasColumnName("adnSubscriptionStatus");
            entity.Property(e => e.AssignedAgegroupId).HasColumnName("assigned_agegroupID");
            entity.Property(e => e.AssignedCustomerId).HasColumnName("assigned_customerID");
            entity.Property(e => e.AssignedDivId).HasColumnName("assigned_divID");
            entity.Property(e => e.AssignedLeagueId).HasColumnName("assigned_leagueID");
            entity.Property(e => e.AssignedTeamId).HasColumnName("assigned_teamID");
            entity.Property(e => e.Assignment).HasColumnName("assignment");
            entity.Property(e => e.BActive).HasColumnName("bActive");
            entity.Property(e => e.BBgcheck).HasColumnName("bBGCheck");
            entity.Property(e => e.BCollegeCommit)
                .HasDefaultValue(false)
                .HasColumnName("bCollegeCommit");
            entity.Property(e => e.BConfirmationSent).HasColumnName("bConfirmationSent");
            entity.Property(e => e.BMedAlert).HasColumnName("bMedAlert");
            entity.Property(e => e.BScholarshipRequested).HasColumnName("bScholarshipRequested");
            entity.Property(e => e.BTravel).HasColumnName("bTravel");
            entity.Property(e => e.BUploadedInsuranceCard)
                .HasDefaultValue(false)
                .HasColumnName("bUploadedInsuranceCard");
            entity.Property(e => e.BUploadedMedForm)
                .HasDefaultValue(false)
                .HasColumnName("bUploadedMedForm");
            entity.Property(e => e.BUploadedVaccineCard)
                .HasDefaultValue(false)
                .HasColumnName("bUploadedVaccineCard");
            entity.Property(e => e.BWaiverSigned1).HasColumnName("bWaiverSigned1");
            entity.Property(e => e.BWaiverSigned2).HasColumnName("bWaiverSigned2");
            entity.Property(e => e.BWaiverSigned3).HasColumnName("bWaiverSigned3");
            entity.Property(e => e.BWaiverSignedCv19)
                .HasDefaultValue(false)
                .HasColumnName("bWaiverSignedCV19");
            entity.Property(e => e.BackcheckExplain).HasColumnName("backcheck_explain");
            entity.Property(e => e.BgCheckDate).HasColumnName("bgCheckDate");
            entity.Property(e => e.CertDate).HasColumnName("certDate");
            entity.Property(e => e.CertNo).HasColumnName("certNo");
            entity.Property(e => e.ClassRank).HasColumnName("class_rank");
            entity.Property(e => e.ClubCoach)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("club_coach");
            entity.Property(e => e.ClubCoachEmail)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("club_coach_email");
            entity.Property(e => e.ClubName).HasColumnName("club_name");
            entity.Property(e => e.ClubTeamName).IsUnicode(false);
            entity.Property(e => e.CollegeCommit)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("college_commit");
            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");
            entity.Property(e => e.DadInstagram)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Dad_Instagram");
            entity.Property(e => e.DadTwitter)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Dad_Twitter");
            entity.Property(e => e.DayGroup).HasColumnName("dayGroup");
            entity.Property(e => e.DiscountCodeId).HasColumnName("DiscountCodeID");
            entity.Property(e => e.FamilyUserId)
                .HasMaxLength(450)
                .HasColumnName("Family_UserId");
            entity.Property(e => e.Fastestshot).HasColumnName("fastestshot");
            entity.Property(e => e.FeeBase)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_base");
            entity.Property(e => e.FeeDiscount)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_discount");
            entity.Property(e => e.FeeDiscountMp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_discount_mp");
            entity.Property(e => e.FeeDonation)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_donation");
            entity.Property(e => e.FeeLatefee)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_latefee");
            entity.Property(e => e.FeeProcessing)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_processing");
            entity.Property(e => e.FeeTotal)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_total");
            entity.Property(e => e.FiveTenFive).HasColumnName("five_ten_five");
            entity.Property(e => e.Fourtyyarddash).HasColumnName("fourtyyarddash");
            entity.Property(e => e.Gloves).HasColumnName("gloves");
            entity.Property(e => e.Gpa).HasColumnName("gpa");
            entity.Property(e => e.GradYear).HasColumnName("grad_year");
            entity.Property(e => e.HeadshotPath)
                .HasMaxLength(256)
                .IsUnicode(false)
                .HasColumnName("headshot_path");
            entity.Property(e => e.HealthInsurer).HasColumnName("health_insurer");
            entity.Property(e => e.HealthInsurerGroupNo).HasColumnName("health_insurer_group_no");
            entity.Property(e => e.HealthInsurerPhone).HasColumnName("health_insurer_phone");
            entity.Property(e => e.HealthInsurerPolicyNo).HasColumnName("health_insurer_policy_no");
            entity.Property(e => e.Height)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("height");
            entity.Property(e => e.HeightInches).HasColumnName("height_inches");
            entity.Property(e => e.HonorsAcademic)
                .IsUnicode(false)
                .HasColumnName("honors_academic");
            entity.Property(e => e.HonorsAthletic)
                .IsUnicode(false)
                .HasColumnName("honors_athletic");
            entity.Property(e => e.Instagram)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.InsuredName).HasColumnName("insured_name");
            entity.Property(e => e.JerseySize).HasColumnName("jersey_size");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.Kilt).HasColumnName("kilt");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.MedicalNote).HasColumnName("medical_note");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())", "DF__Registrat__modif__3E2826D9")
                .HasColumnName("modified");
            entity.Property(e => e.ModifiedMobile).HasColumnName("modified_mobile");
            entity.Property(e => e.MomInstagram)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Mom_Instagram");
            entity.Property(e => e.MomTwitter)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Mom_Twitter");
            entity.Property(e => e.NightGroup).HasColumnName("nightGroup");
            entity.Property(e => e.OtherSports)
                .IsUnicode(false)
                .HasColumnName("other_sports");
            entity.Property(e => e.OwedTotal)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("owed_total");
            entity.Property(e => e.PaidTotal)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("paid_total");
            entity.Property(e => e.Position).HasColumnName("position");
            entity.Property(e => e.Psat).HasColumnName("psat");
            entity.Property(e => e.RecruitingHandle).IsUnicode(false);
            entity.Property(e => e.RegformId).HasColumnName("regformId");
            entity.Property(e => e.Region).HasColumnName("region");
            entity.Property(e => e.RegistrationAi)
                .ValueGeneratedOnAdd()
                .HasColumnName("RegistrationAI");
            entity.Property(e => e.RegistrationFormName).HasColumnName("registrationFormName");
            entity.Property(e => e.RegistrationGroupId).HasColumnName("RegistrationGroupID");
            entity.Property(e => e.RegistrationTs).HasColumnName("RegistrationTS");
            entity.Property(e => e.RegsaverPolicyId).HasColumnName("regsaverPolicyId");
            entity.Property(e => e.RegsaverPolicyIdCreateDate)
                .HasColumnType("datetime")
                .HasColumnName("regsaverPolicyIdCreateDate");
            entity.Property(e => e.RequestedAgegroupId).HasColumnName("requestedAgegroupID");
            entity.Property(e => e.Reversible).HasColumnName("reversible");
            entity.Property(e => e.RoleId).HasMaxLength(450);
            entity.Property(e => e.RoommatePref).HasColumnName("roommate_pref");
            entity.Property(e => e.Sat).HasColumnName("sat");
            entity.Property(e => e.SatMath).HasColumnName("satMath");
            entity.Property(e => e.SatVerbal).HasColumnName("satVerbal");
            entity.Property(e => e.SatWriting).HasColumnName("satWriting");
            entity.Property(e => e.SchoolActivities)
                .IsUnicode(false)
                .HasColumnName("school_activities");
            entity.Property(e => e.SchoolCoach)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("school_coach");
            entity.Property(e => e.SchoolCoachEmail)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("school_coach_email");
            entity.Property(e => e.SchoolGrade).HasColumnName("school_grade");
            entity.Property(e => e.SchoolLevelClasses)
                .HasMaxLength(256)
                .IsUnicode(false)
                .HasColumnName("school_level_classes");
            entity.Property(e => e.SchoolName).HasColumnName("school_name");
            entity.Property(e => e.SchoolTeamName).HasColumnName("school_team_name");
            entity.Property(e => e.Shoes).HasColumnName("shoes");
            entity.Property(e => e.ShortsSize).HasColumnName("shorts_size");
            entity.Property(e => e.SkillLevel)
                .IsUnicode(false)
                .HasColumnName("skill_level");
            entity.Property(e => e.Snapchat)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.SpecialRequests).HasColumnName("specialRequests");
            entity.Property(e => e.SportAssnId).HasColumnName("sportAssnID");
            entity.Property(e => e.SportAssnIdexpDate).HasColumnName("sportAssnIDExpDate");
            entity.Property(e => e.SportYearsExp).HasColumnName("sport_years_exp");
            entity.Property(e => e.StrongHand)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("strong_hand");
            entity.Property(e => e.Sweatpants).HasColumnName("sweatpants");
            entity.Property(e => e.Sweatshirt).HasColumnName("sweatshirt");
            entity.Property(e => e.TShirt).HasColumnName("t-shirt");
            entity.Property(e => e.Threehundredshuttle).HasColumnName("threehundredshuttle");
            entity.Property(e => e.TikTokHandle).IsUnicode(false);
            entity.Property(e => e.Twitter)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.UniformNo).HasColumnName("uniform_no");
            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.VolChildreninprogram).HasColumnName("vol_childreninprogram");
            entity.Property(e => e.Volposition).HasColumnName("volposition");
            entity.Property(e => e.WeightLbs).HasColumnName("weight_lbs");

            entity.HasOne(d => d.AssignedTeam).WithMany(p => p.Registrations)
                .HasForeignKey(d => d.AssignedTeamId)
                .HasConstraintName("fk_assignedteam");

            entity.HasOne(d => d.DiscountCode).WithMany(p => p.Registrations)
                .HasForeignKey(d => d.DiscountCodeId)
                .HasConstraintName("FK__Registrat__Disco__239F1926");

            entity.HasOne(d => d.FamilyUser).WithMany(p => p.Registrations)
                .HasForeignKey(d => d.FamilyUserId)
                .HasConstraintName("FK_Registrations_Families");

            entity.HasOne(d => d.Job).WithMany(p => p.Registrations)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs.Registrations_Jobs.Jobs_jobID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.RegistrationsLebUser)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Jobs.Registrations_AspNetUsers_lebUserID");

            entity.HasOne(d => d.Regform).WithMany(p => p.Registrations)
                .HasForeignKey(d => d.RegformId)
                .HasConstraintName("FK__Registrat__regfo__2AE1DEE9");

            entity.HasOne(d => d.Role).WithMany(p => p.Registrations)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("FK_Jobs.Registrations_AspNetRoles_RoleId");

            entity.HasOne(d => d.User).WithMany(p => p.RegistrationsUser)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Jobs.Registrations_AspNetUsers_UserId");
        });

        modelBuilder.Entity<ReportExportTypes>(entity =>
        {
            entity.HasKey(e => e.ReportExportTypeId).HasName("PK_reference.ReportExportTypes");

            entity.ToTable("ReportExportTypes", "reference");

            entity.Property(e => e.ReportExportTypeId).HasColumnName("ReportExportTypeID");
        });

        modelBuilder.Entity<Reports>(entity =>
        {
            entity.HasKey(e => e.ReportName).HasName("PK_reference.Reports");

            entity.ToTable("Reports", "reference");

            entity.Property(e => e.ReportName).HasColumnName("reportName");
        });

        modelBuilder.Entity<Schedule>(entity =>
        {
            entity.HasKey(e => e.Gid).HasName("PK__schedule__C51F0F3E715F5D79");

            entity.ToTable("schedule", "Leagues");

            entity.Property(e => e.Gid).HasColumnName("GID");
            entity.Property(e => e.AgegroupId).HasColumnName("agegroupID");
            entity.Property(e => e.AgegroupName).HasColumnName("agegroupName");
            entity.Property(e => e.Div2Id).HasColumnName("div2ID");
            entity.Property(e => e.Div2Name).HasColumnName("div2Name");
            entity.Property(e => e.DivId).HasColumnName("divID");
            entity.Property(e => e.DivName).HasColumnName("divName");
            entity.Property(e => e.FName).HasColumnName("fName");
            entity.Property(e => e.FieldId).HasColumnName("fieldID");
            entity.Property(e => e.GDate)
                .HasColumnType("datetime")
                .HasColumnName("G_Date");
            entity.Property(e => e.GNo).HasColumnName("G_No");
            entity.Property(e => e.GStatusCode).HasColumnName("g_statusCode");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.LeagueId).HasColumnName("leagueID");
            entity.Property(e => e.LeagueName).HasColumnName("leagueName");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.RefCount)
                .HasColumnType("decimal(2, 1)")
                .HasColumnName("ref_count");
            entity.Property(e => e.RescheduleCount).HasColumnName("rescheduleCount");
            entity.Property(e => e.Rnd).HasColumnName("rnd");
            entity.Property(e => e.Season)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.T1Ann)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("t1_ann");
            entity.Property(e => e.T1CalcType)
                .IsUnicode(false)
                .HasColumnName("T1_CalcType");
            entity.Property(e => e.T1GnoRef).HasColumnName("T1_GnoRef");
            entity.Property(e => e.T1Id).HasColumnName("T1_ID");
            entity.Property(e => e.T1Name)
                .IsUnicode(false)
                .HasColumnName("T1_Name");
            entity.Property(e => e.T1No).HasColumnName("T1_No");
            entity.Property(e => e.T1Score).HasColumnName("T1_Score");
            entity.Property(e => e.T1Type)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("T1_Type");
            entity.Property(e => e.T1penalties).HasColumnName("t1penalties");
            entity.Property(e => e.T2Ann)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("t2_ann");
            entity.Property(e => e.T2CalcType)
                .IsUnicode(false)
                .HasColumnName("T2_CalcType");
            entity.Property(e => e.T2GnoRef).HasColumnName("T2_GNoRef");
            entity.Property(e => e.T2Id).HasColumnName("T2_ID");
            entity.Property(e => e.T2Name)
                .IsUnicode(false)
                .HasColumnName("T2_Name");
            entity.Property(e => e.T2No).HasColumnName("T2_No");
            entity.Property(e => e.T2Score).HasColumnName("T2_Score");
            entity.Property(e => e.T2Type)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("T2_Type");
            entity.Property(e => e.T2penalties).HasColumnName("t2penalties");
            entity.Property(e => e.Year)
                .HasMaxLength(4)
                .IsUnicode(false)
                .IsFixedLength();

            entity.HasOne(d => d.Agegroup).WithMany(p => p.Schedule)
                .HasForeignKey(d => d.AgegroupId)
                .HasConstraintName("FK__schedule__agegro__74E42A3D");

            entity.HasOne(d => d.Div2).WithMany(p => p.ScheduleDiv2)
                .HasForeignKey(d => d.Div2Id)
                .HasConstraintName("FK__schedule__div2ID__76CC72AF");

            entity.HasOne(d => d.Div).WithMany(p => p.ScheduleDiv)
                .HasForeignKey(d => d.DivId)
                .HasConstraintName("FK__schedule__divID__75D84E76");

            entity.HasOne(d => d.Field).WithMany(p => p.Schedule)
                .HasForeignKey(d => d.FieldId)
                .HasConstraintName("FK__schedule__fieldI__78B4BB21");

            entity.HasOne(d => d.GStatusCodeNavigation).WithMany(p => p.Schedule)
                .HasForeignKey(d => d.GStatusCode)
                .HasConstraintName("FK__schedule__g_stat__77C096E8");

            entity.HasOne(d => d.Job).WithMany(p => p.Schedule)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__schedule__jobID__1B09D325");

            entity.HasOne(d => d.League).WithMany(p => p.Schedule)
                .HasForeignKey(d => d.LeagueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__schedule__league__73F00604");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Schedule)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__schedule__lebUse__7F61B8B0");

            entity.HasOne(d => d.T1GnoRefNavigation).WithMany(p => p.InverseT1GnoRefNavigation)
                .HasForeignKey(d => d.T1GnoRef)
                .HasConstraintName("FK__schedule__T1_Gno__7B9127CC");

            entity.HasOne(d => d.T1).WithMany(p => p.ScheduleT1)
                .HasForeignKey(d => d.T1Id)
                .HasConstraintName("FK__schedule__T1_ID__7A9D0393");

            entity.HasOne(d => d.T1TypeNavigation).WithMany(p => p.ScheduleT1TypeNavigation)
                .HasForeignKey(d => d.T1Type)
                .HasConstraintName("FK__schedule__T1_Typ__79A8DF5A");

            entity.HasOne(d => d.T2GnoRefNavigation).WithMany(p => p.InverseT2GnoRefNavigation)
                .HasForeignKey(d => d.T2GnoRef)
                .HasConstraintName("FK__schedule__T2_GNo__7E6D9477");

            entity.HasOne(d => d.T2).WithMany(p => p.ScheduleT2)
                .HasForeignKey(d => d.T2Id)
                .HasConstraintName("FK__schedule__T2_ID__7D79703E");

            entity.HasOne(d => d.T2TypeNavigation).WithMany(p => p.ScheduleT2TypeNavigation)
                .HasForeignKey(d => d.T2Type)
                .HasConstraintName("FK__schedule__T2_Typ__7C854C05");
        });

        modelBuilder.Entity<ScheduleTeamTypes>(entity =>
        {
            entity.HasKey(e => e.TeamTypeId)
                .HasName("PK__schedule__31CF224DADD5D674")
                .HasFillFactor(60);

            entity.ToTable("scheduleTeamTypes", "reference");

            entity.Property(e => e.TeamTypeId)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("teamTypeID");
            entity.Property(e => e.TeamTypeDesc)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("teamTypeDesc");
        });

        modelBuilder.Entity<Sliders>(entity =>
        {
            entity.HasKey(e => e.SliderId).HasName("PK__Sliders__24BC96F0A48FF23B");

            entity.ToTable("Sliders", "Jobs");

            entity.Property(e => e.SliderId).ValueGeneratedNever();
            entity.Property(e => e.BackgroundImageUrl).IsUnicode(false);

            entity.HasOne(d => d.Job).WithMany(p => p.Sliders)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK__Sliders__JobId__3A592CA3");
        });

        modelBuilder.Entity<Slides>(entity =>
        {
            entity.HasKey(e => e.SlideId).HasName("PK__Slides__9E7CB65095583B3E");

            entity.ToTable("Slides", "Jobs");

            entity.Property(e => e.SlideId).ValueGeneratedNever();
            entity.Property(e => e.ImageUrl).IsUnicode(false);
            entity.Property(e => e.Subtitle).IsUnicode(false);
            entity.Property(e => e.Text).IsUnicode(false);
            entity.Property(e => e.Title).IsUnicode(false);

            entity.HasOne(d => d.Slider).WithMany(p => p.Slides)
                .HasForeignKey(d => d.SliderId)
                .HasConstraintName("FK__Slides__SliderId__3D35994E");
        });

        modelBuilder.Entity<Sports>(entity =>
        {
            entity.HasKey(e => e.SportId).HasName("PK_reference.Sports");

            entity.ToTable("Sports", "reference");

            entity.Property(e => e.SportId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("sportID");
            entity.Property(e => e.Ai)
                .ValueGeneratedOnAdd()
                .HasColumnName("ai");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.SportName).HasColumnName("sportName");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Sports)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_reference.Sports_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<StandingsSortProfileRules>(entity =>
        {
            entity.HasKey(e => e.StandingsSortProfileRuleId).HasName("PK__Standing__7B66A9304CDBCDBC");

            entity.ToTable("StandingsSortProfileRules", "Leagues");

            entity.HasOne(d => d.StandingsSortProfile).WithMany(p => p.StandingsSortProfileRules)
                .HasForeignKey(d => d.StandingsSortProfileId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Standings__Stand__01C0991C");

            entity.HasOne(d => d.StandingsSortRule).WithMany(p => p.StandingsSortProfileRules)
                .HasForeignKey(d => d.StandingsSortRuleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Standings__Stand__02B4BD55");
        });

        modelBuilder.Entity<StandingsSortProfiles>(entity =>
        {
            entity.HasKey(e => e.StandingsSortProfileId).HasName("PK__Standing__32B407CCDA512B0C");

            entity.ToTable("StandingsSortProfiles", "Leagues");

            entity.Property(e => e.StandingsSortProfileName).IsUnicode(false);
        });

        modelBuilder.Entity<StandingsSortRules>(entity =>
        {
            entity.HasKey(e => e.StandingsSortRuleId).HasName("PK__Standing__DBA1E94A872FDD45");

            entity.ToTable("StandingsSortRules", "Leagues");

            entity.Property(e => e.StandingsSortRuleDescription).IsUnicode(false);
            entity.Property(e => e.StandingsSortRuleName).IsUnicode(false);
        });

        modelBuilder.Entity<States>(entity =>
        {
            entity.HasKey(e => e.StateId).HasName("PK_reference.States");

            entity.ToTable("States", "reference");

            entity.Property(e => e.StateId).HasColumnName("StateID");
        });

        modelBuilder.Entity<StoreCart>(entity =>
        {
            entity.HasKey(e => e.StoreCartId).HasName("PK__StoreCar__1A43BAEF4E7A8569");

            entity.ToTable("StoreCart", "stores");

            entity.Property(e => e.FamilyUserId).HasMaxLength(450);
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.FamilyUser).WithMany(p => p.StoreCartFamilyUser)
                .HasForeignKey(d => d.FamilyUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Famil__19431CF2");

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreCartLebUser)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__lebUs__1B2B6564");

            entity.HasOne(d => d.Store).WithMany(p => p.StoreCart)
                .HasForeignKey(d => d.StoreId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Store__184EF8B9");
        });

        modelBuilder.Entity<StoreCartBatchAccounting>(entity =>
        {
            entity.HasKey(e => e.StoreCartBatchAccountingId).HasName("PK__StoreCar__2BD6BB4A63472E35");

            entity.ToTable("StoreCartBatchAccounting", "stores");

            entity.Property(e => e.AdnInvoiceNo).IsUnicode(false);
            entity.Property(e => e.AdnTransactionId).IsUnicode(false);
            entity.Property(e => e.CcexpDate)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("CCExpDate");
            entity.Property(e => e.Cclast4)
                .HasMaxLength(4)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("CCLast4");
            entity.Property(e => e.Comment).IsUnicode(false);
            entity.Property(e => e.CreateDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.Paid).HasColumnType("money");

            entity.HasOne(d => d.DiscountCodeAiNavigation).WithMany(p => p.StoreCartBatchAccounting)
                .HasForeignKey(d => d.DiscountCodeAi)
                .HasConstraintName("FK__StoreCart__Disco__2E3E39D8");

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreCartBatchAccounting)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__lebUs__3026824A");

            entity.HasOne(d => d.PaymentMethod).WithMany(p => p.StoreCartBatchAccounting)
                .HasForeignKey(d => d.PaymentMethodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Payme__2C55F166");

            entity.HasOne(d => d.StoreCartBatch).WithMany(p => p.StoreCartBatchAccounting)
                .HasForeignKey(d => d.StoreCartBatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Store__2B61CD2D");
        });

        modelBuilder.Entity<StoreCartBatchSkuEdits>(entity =>
        {
            entity.HasKey(e => e.StoreCartBatchSkuEditId).HasName("PK__StoreCar__8D150EFE9279EF74");

            entity.ToTable("StoreCartBatchSkuEdits", "stores");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified).HasColumnName("modified");

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreCartBatchSkuEdits)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__lebUs__509351DC");

            entity.HasOne(d => d.PreviousStoreCartBatchSku).WithMany(p => p.StoreCartBatchSkuEditsPreviousStoreCartBatchSku)
                .HasForeignKey(d => d.PreviousStoreCartBatchSkuId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Previ__4F9F2DA3");

            entity.HasOne(d => d.StoreCartBatchSku).WithMany(p => p.StoreCartBatchSkuEditsStoreCartBatchSku)
                .HasForeignKey(d => d.StoreCartBatchSkuId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Store__4EAB096A");
        });

        modelBuilder.Entity<StoreCartBatchSkuQuantityAdjustments>(entity =>
        {
            entity.HasKey(e => e.StoreCartBatchSkuQuantityAdjustmentsId).HasName("PK__StoreCar__F74DB8B94CF26B7F");

            entity.ToTable("StoreCartBatchSkuQuantityAdjustments", "stores");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified).HasColumnName("modified");

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreCartBatchSkuQuantityAdjustments)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__lebUs__555806F9");

            entity.HasOne(d => d.StoreCart).WithMany(p => p.StoreCartBatchSkuQuantityAdjustments)
                .HasForeignKey(d => d.StoreCartId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Store__536FBE87");

            entity.HasOne(d => d.StoreSku).WithMany(p => p.StoreCartBatchSkuQuantityAdjustments)
                .HasForeignKey(d => d.StoreSkuId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Store__5463E2C0");
        });

        modelBuilder.Entity<StoreCartBatchSkuRestocks>(entity =>
        {
            entity.HasKey(e => e.StoreCartBatchSkuRestockId).HasName("PK__StoreCar__5498C998ED4AD5FC");

            entity.ToTable("StoreCartBatchSkuRestocks", "stores");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified).HasColumnName("modified");

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreCartBatchSkuRestocks)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__lebUs__592897DD");

            entity.HasOne(d => d.StoreCartBatchSku).WithMany(p => p.StoreCartBatchSkuRestocks)
                .HasForeignKey(d => d.StoreCartBatchSkuId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Store__583473A4");
        });

        modelBuilder.Entity<StoreCartBatchSkus>(entity =>
        {
            entity.HasKey(e => e.StoreCartBatchSkuId).HasName("PK__StoreCar__C353F9D3D06FB223");

            entity.ToTable("StoreCartBatchSkus", "stores");

            entity.Property(e => e.CreateDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.FeeProcessing).HasColumnType("money");
            entity.Property(e => e.FeeProduct).HasColumnType("money");
            entity.Property(e => e.FeeTotal).HasColumnType("money");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.PaidTotal).HasColumnType("money");
            entity.Property(e => e.RefundedTotal).HasColumnType("money");
            entity.Property(e => e.SalesTax).HasColumnType("money");
            entity.Property(e => e.UnitPrice).HasColumnType("money");

            entity.HasOne(d => d.DirectToReg).WithMany(p => p.StoreCartBatchSkus)
                .HasForeignKey(d => d.DirectToRegId)
                .HasConstraintName("FK__StoreCart__Direc__24B4CF9E");

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreCartBatchSkus)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__lebUs__28856082");

            entity.HasOne(d => d.StoreCartBatch).WithMany(p => p.StoreCartBatchSkus)
                .HasForeignKey(d => d.StoreCartBatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Store__22CC872C");

            entity.HasOne(d => d.StoreSku).WithMany(p => p.StoreCartBatchSkus)
                .HasForeignKey(d => d.StoreSkuId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Store__23C0AB65");
        });

        modelBuilder.Entity<StoreCartBatches>(entity =>
        {
            entity.HasKey(e => e.StoreCartBatchId).HasName("PK__StoreCar__B725097C128D2ABF");

            entity.ToTable("StoreCartBatches", "stores");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.SignedForBy).IsUnicode(false);
            entity.Property(e => e.SignedForDate).HasColumnType("datetime");

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreCartBatches)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__lebUs__1FF01A81");

            entity.HasOne(d => d.StoreCart).WithMany(p => p.StoreCartBatches)
                .HasForeignKey(d => d.StoreCartId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreCart__Store__1E07D20F");
        });

        modelBuilder.Entity<StoreColors>(entity =>
        {
            entity.HasKey(e => e.StoreColorId).HasName("PK__StoreCol__752A186114428311");

            entity.ToTable("StoreColors", "stores");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.StoreColorName).IsUnicode(false);

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreColors)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreColo__lebUs__0EC58E7F");
        });

        modelBuilder.Entity<StoreItemSkus>(entity =>
        {
            entity.HasKey(e => e.StoreSkuId).HasName("PK__StoreIte__FAF9FC84CE79B93A");

            entity.ToTable("StoreItemSkus", "stores");

            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.MaxCanSell).HasColumnName("maxCanSell");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreItemSkus)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreItem__lebUs__15728C0E");

            entity.HasOne(d => d.StoreColor).WithMany(p => p.StoreItemSkus)
                .HasForeignKey(d => d.StoreColorId)
                .HasConstraintName("FK__StoreItem__Store__12961F63");

            entity.HasOne(d => d.StoreItem).WithMany(p => p.StoreItemSkus)
                .HasForeignKey(d => d.StoreItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreItem__Store__11A1FB2A");

            entity.HasOne(d => d.StoreSize).WithMany(p => p.StoreItemSkus)
                .HasForeignKey(d => d.StoreSizeId)
                .HasConstraintName("FK__StoreItem__Store__138A439C");
        });

        modelBuilder.Entity<StoreItems>(entity =>
        {
            entity.HasKey(e => e.StoreItemId).HasName("PK__StoreIte__A9B956582A109454");

            entity.ToTable("StoreItems", "stores");

            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.SortOrder).HasColumnName("sortOrder");
            entity.Property(e => e.StoreItemComments).IsUnicode(false);
            entity.Property(e => e.StoreItemName).IsUnicode(false);
            entity.Property(e => e.StoreItemPrice).HasColumnType("money");

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreItems)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreItem__lebUs__07246CB7");

            entity.HasOne(d => d.Store).WithMany(p => p.StoreItems)
                .HasForeignKey(d => d.StoreId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreItem__Store__053C2445");
        });

        modelBuilder.Entity<StoreSizes>(entity =>
        {
            entity.HasKey(e => e.StoreSizeId).HasName("PK__StoreSiz__71F132A58021EB82");

            entity.ToTable("StoreSizes", "stores");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.StoreSizeName).IsUnicode(false);

            entity.HasOne(d => d.LebUser).WithMany(p => p.StoreSizes)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StoreSize__lebUs__0AF4FD9B");
        });

        modelBuilder.Entity<Stores>(entity =>
        {
            entity.HasKey(e => e.StoreId).HasName("PK__Stores__3B82F10177AE849E");

            entity.ToTable("Stores", "stores");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.Job).WithMany(p => p.Stores)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Stores__JobId__00776F28");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Stores)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Stores__lebUserI__025FB79A");

            entity.HasOne(d => d.ParentStore).WithMany(p => p.InverseParentStore)
                .HasForeignKey(d => d.ParentStoreId)
                .HasConstraintName("FK__Stores__ParentSt__344C18E9");
        });

        modelBuilder.Entity<TeamAttendanceEvents>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__TeamAtte__7944C810396C738D");

            entity.ToTable("TeamAttendanceEvents", "Leagues");

            entity.Property(e => e.Comment).IsUnicode(false);
            entity.Property(e => e.EventDate).HasColumnType("datetime");
            entity.Property(e => e.EventLocation).IsUnicode(false);
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");

            entity.HasOne(d => d.EventType).WithMany(p => p.TeamAttendanceEvents)
                .HasForeignKey(d => d.EventTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamAtten__Event__71D44A16");

            entity.HasOne(d => d.LebUser).WithMany(p => p.TeamAttendanceEvents)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK__TeamAtten__lebUs__72C86E4F");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamAttendanceEvents)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamAtten__TeamI__70E025DD");
        });

        modelBuilder.Entity<TeamAttendanceRecords>(entity =>
        {
            entity.HasKey(e => e.AttendanceId).HasName("PK__TeamAtte__8B69261C18729DC8");

            entity.ToTable("TeamAttendanceRecords", "Leagues");

            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.PlayerId).HasMaxLength(450);

            entity.HasOne(d => d.Event).WithMany(p => p.TeamAttendanceRecords)
                .HasForeignKey(d => d.EventId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamAtten__Event__7698FF33");

            entity.HasOne(d => d.LebUser).WithMany(p => p.TeamAttendanceRecordsLebUser)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK__TeamAtten__lebUs__79756BDE");

            entity.HasOne(d => d.Player).WithMany(p => p.TeamAttendanceRecordsPlayer)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamAtten__Playe__778D236C");
        });

        modelBuilder.Entity<TeamAttendanceTypes>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__TeamAtte__3214EC078014EBAE");

            entity.ToTable("TeamAttendanceTypes", "reference");

            entity.Property(e => e.AttendanceType)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<TeamDocs>(entity =>
        {
            entity.HasKey(e => e.DocId).HasName("PK__Team_Doc__3EF188AD6F040732");

            entity.ToTable("Team_Docs", "mobile");

            entity.Property(e => e.DocId).ValueGeneratedNever();
            entity.Property(e => e.CreateDate).HasColumnType("datetime");
            entity.Property(e => e.DocUrl).IsUnicode(false);
            entity.Property(e => e.Label).IsUnicode(false);
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.Job).WithMany(p => p.TeamDocs)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK__Team_Docs__JobId__67D51339");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamDocs)
                .HasForeignKey(d => d.TeamId)
                .HasConstraintName("FK__Team_Docs__TeamI__66E0EF00");

            entity.HasOne(d => d.User).WithMany(p => p.TeamDocs)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Team_Docs__UserI__68C93772");
        });

        modelBuilder.Entity<TeamEvents>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__Team_Eve__7944C8100111254A");

            entity.ToTable("Team_Events", "mobile");

            entity.Property(e => e.EventId).ValueGeneratedNever();
            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.CreateDate).HasColumnType("datetime");
            entity.Property(e => e.EventDate).HasColumnType("datetime");
            entity.Property(e => e.Label).IsUnicode(false);
            entity.Property(e => e.Location)
                .IsUnicode(false)
                .HasColumnName("location");
            entity.Property(e => e.MinutesDuration).HasColumnName("minutesDuration");
            entity.Property(e => e.Url)
                .IsUnicode(false)
                .HasColumnName("url");
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.Job).WithMany(p => p.TeamEvents)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK__Team_Even__JobId__7F0E49A3");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamEvents)
                .HasForeignKey(d => d.TeamId)
                .HasConstraintName("FK__Team_Even__TeamI__00026DDC");

            entity.HasOne(d => d.User).WithMany(p => p.TeamEvents)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Team_Even__UserI__00F69215");
        });

        modelBuilder.Entity<TeamGalleryPhotos>(entity =>
        {
            entity.HasKey(e => e.PhotoId).HasName("PK__TeamGall__21B7B5E257164179");

            entity.ToTable("TeamGallery_Photos", "mobile");

            entity.Property(e => e.PhotoId).ValueGeneratedNever();
            entity.Property(e => e.Caption).IsUnicode(false);
            entity.Property(e => e.CreateDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.Team).WithMany(p => p.TeamGalleryPhotos)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamGalle__TeamI__457FFB35");

            entity.HasOne(d => d.User).WithMany(p => p.TeamGalleryPhotos)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamGalle__UserI__46741F6E");
        });

        modelBuilder.Entity<TeamMessages>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__MobileBu__3214EC0768FDEBD7");

            entity.ToTable("TeamMessages", "mobile");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.AttachmentUrl)
                .IsUnicode(false)
                .HasColumnName("attachmentUrl");
            entity.Property(e => e.BClubBroadcast).HasColumnName("bClubBroadcast");
            entity.Property(e => e.Content).IsUnicode(false);
            entity.Property(e => e.Createdate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("createdate");
            entity.Property(e => e.DaysVisible)
                .HasDefaultValue(7)
                .HasColumnName("daysVisible");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.PhotoUrl).IsUnicode(false);
            entity.Property(e => e.TeamId).HasColumnName("teamId");
            entity.Property(e => e.Title)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.SenderRegistration).WithMany(p => p.TeamMessages)
                .HasForeignKey(d => d.SenderRegistrationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__MobileBul__Sende__26074FDC");
        });

        modelBuilder.Entity<TeamRosterRequests>(entity =>
        {
            entity.HasKey(e => e.RequestId).HasName("PK__Team_Ros__E3C5DE513728BFD2");

            entity.ToTable("Team_Roster_Requests", "Leagues");

            entity.Property(e => e.RequestId)
                .ValueGeneratedNever()
                .HasColumnName("requestID");
            entity.Property(e => e.FirstName)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("firstName");
            entity.Property(e => e.LastName)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("lastName");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.Position).HasColumnName("position");
            entity.Property(e => e.TeamId).HasColumnName("teamID");
            entity.Property(e => e.UniformNo).HasColumnName("uniform_no");

            entity.HasOne(d => d.LebUser).WithMany(p => p.TeamRosterRequests)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Team_Rost__lebUs__08C105B8");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamRosterRequests)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Team_Rost__teamI__07CCE17F");
        });

        modelBuilder.Entity<TeamSignupEvents>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__TeamSign__7944C810000A8614");

            entity.ToTable("TeamSignupEvents", "mobile");

            entity.Property(e => e.EventId).ValueGeneratedNever();
            entity.Property(e => e.CreateDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EventAi).ValueGeneratedOnAdd();
            entity.Property(e => e.EventCategory).IsUnicode(false);
            entity.Property(e => e.EventComments).IsUnicode(false);
            entity.Property(e => e.EventDate).HasColumnType("datetime");
            entity.Property(e => e.EventEndDate).HasColumnType("datetime");
            entity.Property(e => e.EventWhere).IsUnicode(false);

            entity.HasOne(d => d.CreatorReg).WithMany(p => p.TeamSignupEvents)
                .HasForeignKey(d => d.CreatorRegId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamSignu__Creat__3DDED96D");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamSignupEvents)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamSignu__TeamI__3CEAB534");
        });

        modelBuilder.Entity<TeamSignupEventsRegistrations>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__TeamSign__3213E83FBF2D659E");

            entity.ToTable("TeamSignupEvents_Registrations", "mobile");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.BAccept).HasColumnName("bAccept");
            entity.Property(e => e.BAttend).HasColumnName("bAttend");
            entity.Property(e => e.EventId).HasColumnName("eventId");

            entity.HasOne(d => d.Event).WithMany(p => p.TeamSignupEventsRegistrations)
                .HasForeignKey(d => d.EventId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamSignu__event__41AF6A51");

            entity.HasOne(d => d.Registration).WithMany(p => p.TeamSignupEventsRegistrations)
                .HasForeignKey(d => d.RegistrationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamSignu__Regis__42A38E8A");
        });

        modelBuilder.Entity<TeamTournamentMappings>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__TeamTour__3214EC0767CF7CB8");

            entity.ToTable("TeamTournamentMappings", "Leagues");

            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.HasOne(d => d.ClubTeam).WithMany(p => p.TeamTournamentMappingsClubTeam)
                .HasForeignKey(d => d.ClubTeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamTourn__ClubT__777814D3");

            entity.HasOne(d => d.TournamentTeam).WithMany(p => p.TeamTournamentMappingsTournamentTeam)
                .HasForeignKey(d => d.TournamentTeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__TeamTourn__Tourn__786C390C");
        });

        modelBuilder.Entity<Teams>(entity =>
        {
            entity.HasKey(e => e.TeamId).HasName("PK_Leagues.teams");

            entity.ToTable("teams", "Leagues", tb => tb.HasTrigger("Team_AfterEdit_UpdateTeamAssignments"));

            entity.Property(e => e.TeamId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("teamID");
            entity.Property(e => e.Active).HasColumnName("active");
            entity.Property(e => e.AdnSubscriptionAmountPerOccurence).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.AdnSubscriptionId).IsUnicode(false);
            entity.Property(e => e.AdnSubscriptionStartDate).HasColumnType("datetime");
            entity.Property(e => e.AdnSubscriptionStatus).IsUnicode(false);
            entity.Property(e => e.AgegroupId).HasColumnName("agegroupID");
            entity.Property(e => e.AgegroupRequested).HasColumnName("agegroupRequested");
            entity.Property(e => e.BAllowSelfRostering)
                .HasDefaultValue(false, "DF_teams_bAllowSelfRostering")
                .HasColumnName("bAllowSelfRostering");
            entity.Property(e => e.BDoNotValidateUslaxNumber)
                .HasDefaultValue(false)
                .HasColumnName("bDoNotValidateUSLaxNumber");
            entity.Property(e => e.BHideRoster).HasColumnName("bHideRoster");
            entity.Property(e => e.BnewCoach).HasColumnName("BNewCoach");
            entity.Property(e => e.BnewTeam).HasColumnName("BNewTeam");
            entity.Property(e => e.ClubrepId)
                .HasMaxLength(450)
                .HasColumnName("clubrep_id");
            entity.Property(e => e.ClubrepRegistrationid).HasColumnName("clubrep_registrationid");
            entity.Property(e => e.Color).HasColumnName("color");
            entity.Property(e => e.Createdate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("createdate");
            entity.Property(e => e.CustomerId).HasColumnName("customerID");
            entity.Property(e => e.DiscountCodeId).HasColumnName("DiscountCodeID");
            entity.Property(e => e.DiscountFee)
                .HasColumnType("money")
                .HasColumnName("discountFee");
            entity.Property(e => e.DiscountFeeEnd).HasColumnName("discountFeeEnd");
            entity.Property(e => e.DiscountFeeStart).HasColumnName("discountFeeStart");
            entity.Property(e => e.DisplayName).IsUnicode(false);
            entity.Property(e => e.District).HasColumnName("district");
            entity.Property(e => e.DivId).HasColumnName("divID");
            entity.Property(e => e.DivRank)
                .HasDefaultValue(1)
                .HasColumnName("divRank");
            entity.Property(e => e.DivisionRequested).HasColumnName("divisionRequested");
            entity.Property(e => e.DobMax).HasColumnName("dobMax");
            entity.Property(e => e.DobMin).HasColumnName("dobMin");
            entity.Property(e => e.Dow).HasColumnName("dow");
            entity.Property(e => e.Dow2).HasColumnName("dow2");
            entity.Property(e => e.Effectiveasofdate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("effectiveasofdate");
            entity.Property(e => e.Enddate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("enddate");
            entity.Property(e => e.Expireondate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("expireondate");
            entity.Property(e => e.FeeBase)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_base");
            entity.Property(e => e.FeeDiscount)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_discount");
            entity.Property(e => e.FeeDiscountMp)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_discount_mp");
            entity.Property(e => e.FeeDonation)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_donation");
            entity.Property(e => e.FeeLatefee)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_latefee");
            entity.Property(e => e.FeeProcessing)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_processing");
            entity.Property(e => e.FeeTotal)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("fee_total");
            entity.Property(e => e.FieldId1).HasColumnName("fieldID1");
            entity.Property(e => e.FieldId2).HasColumnName("fieldID2");
            entity.Property(e => e.FieldId3).HasColumnName("fieldID3");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.GradYearMax).HasColumnName("grad_year_max");
            entity.Property(e => e.GradYearMin).HasColumnName("grad_year_min");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.KeywordPairs)
                .IsUnicode(false)
                .HasColumnName("keywordPairs");
            entity.Property(e => e.LastLeagueRecord).HasColumnName("lastLeagueRecord");
            entity.Property(e => e.LastSeasonYear).IsUnicode(false);
            entity.Property(e => e.LateFee)
                .HasColumnType("money")
                .HasColumnName("lateFee");
            entity.Property(e => e.LateFeeEnd).HasColumnName("lateFeeEnd");
            entity.Property(e => e.LateFeeStart).HasColumnName("lateFeeStart");
            entity.Property(e => e.LeagueId).HasColumnName("leagueID");
            entity.Property(e => e.LeagueTeamId).HasColumnName("leagueTeamID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.LevelOfPlay).HasColumnName("level_of_play");
            entity.Property(e => e.MaxCount)
                .HasDefaultValue(100)
                .HasColumnName("maxCount");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.OldCoach).IsUnicode(false);
            entity.Property(e => e.OldTeamName).IsUnicode(false);
            entity.Property(e => e.OwedTotal)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("owed_total");
            entity.Property(e => e.PaidTotal)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("paid_total");
            entity.Property(e => e.PerRegistrantDeposit)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("perRegistrantDeposit");
            entity.Property(e => e.PerRegistrantFee)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("perRegistrantFee");
            entity.Property(e => e.PrevTeamId).HasColumnName("prevTeamID");
            entity.Property(e => e.Requests).HasColumnName("requests");
            entity.Property(e => e.SchoolGradeMax).HasColumnName("school_grade_max");
            entity.Property(e => e.SchoolGradeMin).HasColumnName("school_grade_min");
            entity.Property(e => e.Season).HasColumnName("season");
            entity.Property(e => e.StandingsRank).HasColumnName("standingsRank");
            entity.Property(e => e.Startdate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("startdate");
            entity.Property(e => e.TbKey).HasColumnName("tb_key");
            entity.Property(e => e.TeamAi)
                .ValueGeneratedOnAdd()
                .HasColumnName("teamAI");
            entity.Property(e => e.TeamComments).HasColumnName("team_comments");
            entity.Property(e => e.TeamFullName).HasColumnName("teamFullName");
            entity.Property(e => e.TeamName).HasColumnName("teamName");
            entity.Property(e => e.TeamNumber).HasColumnName("team_number");
            entity.Property(e => e.ViPolicyClubRepRegId).HasColumnName("viPolicyClubRepRegId");
            entity.Property(e => e.ViPolicyCreateDate)
                .HasColumnType("datetime")
                .HasColumnName("viPolicyCreateDate");
            entity.Property(e => e.ViPolicyId)
                .IsUnicode(false)
                .HasColumnName("viPolicyId");
            entity.Property(e => e.Year).HasColumnName("year");

            entity.HasOne(d => d.Agegroup).WithMany(p => p.Teams)
                .HasForeignKey(d => d.AgegroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Leagues.teams_Leagues.agegroups_agegroupID");

            entity.HasOne(d => d.ClubTeam).WithMany(p => p.Teams)
                .HasForeignKey(d => d.ClubTeamId)
                .HasConstraintName("FK__teams__ClubTeamI__2B81C8BE");

            entity.HasOne(d => d.Clubrep).WithMany(p => p.TeamsClubrep)
                .HasForeignKey(d => d.ClubrepId)
                .HasConstraintName("FK__teams__clubrep_i__0B7289DA");

            entity.HasOne(d => d.ClubrepRegistration).WithMany(p => p.TeamsClubrepRegistration)
                .HasForeignKey(d => d.ClubrepRegistrationid)
                .HasConstraintName("FK__teams__clubrep_r__0C66AE13");

            entity.HasOne(d => d.Customer).WithMany(p => p.Teams)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK_Leagues.teams_Jobs.Customers_customerID");

            entity.HasOne(d => d.DiscountCode).WithMany(p => p.Teams)
                .HasForeignKey(d => d.DiscountCodeId)
                .HasConstraintName("FK_teams_Job_DiscountCodes");

            entity.HasOne(d => d.Div).WithMany(p => p.Teams)
                .HasForeignKey(d => d.DivId)
                .HasConstraintName("FK_Leagues.teams_Leagues.divisions_divID");

            entity.HasOne(d => d.FieldId1Navigation).WithMany(p => p.TeamsFieldId1Navigation)
                .HasForeignKey(d => d.FieldId1)
                .HasConstraintName("FK_Leagues.teams_reference.Fields_fieldID1");

            entity.HasOne(d => d.FieldId2Navigation).WithMany(p => p.TeamsFieldId2Navigation)
                .HasForeignKey(d => d.FieldId2)
                .HasConstraintName("FK_Leagues.teams_reference.Fields_fieldID2");

            entity.HasOne(d => d.FieldId3Navigation).WithMany(p => p.TeamsFieldId3Navigation)
                .HasForeignKey(d => d.FieldId3)
                .HasConstraintName("FK_Leagues.teams_reference.Fields_fieldID3");

            entity.HasOne(d => d.Job).WithMany(p => p.Teams)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Leagues.teams_Jobs.Jobs_jobID");

            entity.HasOne(d => d.League).WithMany(p => p.Teams)
                .HasForeignKey(d => d.LeagueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Leagues.teams_Leagues.leagues_leagueID");

            entity.HasOne(d => d.LebUser).WithMany(p => p.TeamsLebUser)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_Leagues.teams_AspNetUsers_lebUserID");

            entity.HasOne(d => d.ViPolicyClubRepReg).WithMany(p => p.TeamsViPolicyClubRepReg)
                .HasForeignKey(d => d.ViPolicyClubRepRegId)
                .HasConstraintName("FK__teams__viPolicyC__700BFD35");
        });

        modelBuilder.Entity<Themes>(entity =>
        {
            entity.HasKey(e => e.Theme).HasName("PK_reference.themes");

            entity.ToTable("themes", "reference");

            entity.Property(e => e.Theme).HasColumnName("theme");
        });

        modelBuilder.Entity<TimeslotsLeagueSeasonDates>(entity =>
        {
            entity.HasKey(e => e.Ai).HasName("PK__Timeslot__3213A922E44D6535");

            entity.ToTable("Timeslots_LeagueSeason_Dates", "Leagues");

            entity.Property(e => e.Ai).HasColumnName("ai");
            entity.Property(e => e.AgegroupId).HasColumnName("agegroupID");
            entity.Property(e => e.DivId).HasColumnName("divID");
            entity.Property(e => e.GDate)
                .HasColumnType("datetime")
                .HasColumnName("gDate");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.Rnd).HasColumnName("rnd");
            entity.Property(e => e.Season)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("season");
            entity.Property(e => e.Year)
                .HasMaxLength(4)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("year");

            entity.HasOne(d => d.Agegroup).WithMany(p => p.TimeslotsLeagueSeasonDates)
                .HasForeignKey(d => d.AgegroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Timeslots__agegr__471D5F8D");

            entity.HasOne(d => d.Div).WithMany(p => p.TimeslotsLeagueSeasonDates)
                .HasForeignKey(d => d.DivId)
                .HasConstraintName("FK__Timeslots__divID__481183C6");

            entity.HasOne(d => d.LebUser).WithMany(p => p.TimeslotsLeagueSeasonDates)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Timeslots__lebUs__4905A7FF");
        });

        modelBuilder.Entity<TimeslotsLeagueSeasonFields>(entity =>
        {
            entity.HasKey(e => e.Ai).HasName("PK__Timeslot__3213A922A29BA221");

            entity.ToTable("Timeslots_LeagueSeason_Fields", "Leagues");

            entity.Property(e => e.Ai).HasColumnName("ai");
            entity.Property(e => e.AgegroupId).HasColumnName("agegroupID");
            entity.Property(e => e.DivId).HasColumnName("divID");
            entity.Property(e => e.Dow)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("DOW");
            entity.Property(e => e.FieldId).HasColumnName("fieldID");
            entity.Property(e => e.GamestartInterval).HasColumnName("gamestartInterval");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.MaxGamesPerField).HasColumnName("maxGamesPerField");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.Season)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("season");
            entity.Property(e => e.StartTime)
                .IsUnicode(false)
                .HasColumnName("startTime");
            entity.Property(e => e.Year)
                .HasMaxLength(4)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("year");

            entity.HasOne(d => d.Agegroup).WithMany(p => p.TimeslotsLeagueSeasonFields)
                .HasForeignKey(d => d.AgegroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Timeslots__agegr__407061FE");

            entity.HasOne(d => d.Div).WithMany(p => p.TimeslotsLeagueSeasonFields)
                .HasForeignKey(d => d.DivId)
                .HasConstraintName("FK__Timeslots__divID__4258AA70");

            entity.HasOne(d => d.Field).WithMany(p => p.TimeslotsLeagueSeasonFields)
                .HasForeignKey(d => d.FieldId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Timeslots__field__41648637");

            entity.HasOne(d => d.LebUser).WithMany(p => p.TimeslotsLeagueSeasonFields)
                .HasForeignKey(d => d.LebUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Timeslots__lebUs__434CCEA9");
        });

        modelBuilder.Entity<Timezones>(entity =>
        {
            entity.HasKey(e => e.TzId).HasName("PK_reference.Timezones");

            entity.ToTable("Timezones", "reference");

            entity.Property(e => e.TzId).HasColumnName("TZ_ID");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserID");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("modified");
            entity.Property(e => e.TzName).HasColumnName("TZ_Name");
            entity.Property(e => e.UtcOffset).HasColumnName("UTC_Offset");
            entity.Property(e => e.UtcOffsetHours).HasColumnName("UTC_Offset_Hours");

            entity.HasOne(d => d.LebUser).WithMany(p => p.Timezones)
                .HasForeignKey(d => d.LebUserId)
                .HasConstraintName("FK_reference.Timezones_AspNetUsers_lebUserID");
        });

        modelBuilder.Entity<Txs>(entity =>
        {
            entity.HasKey(e => e.TransactionId).HasFillFactor(60);

            entity.ToTable("Txs", "adn");

            entity.Property(e => e.TransactionId)
                .HasMaxLength(50)
                .HasColumnName("Transaction ID");
            entity.Property(e => e.Address).HasMaxLength(100);
            entity.Property(e => e.AddressVerificationStatus)
                .HasMaxLength(100)
                .HasColumnName("Address Verification Status");
            entity.Property(e => e.AuthorizationAmount)
                .HasMaxLength(50)
                .HasColumnName("Authorization Amount");
            entity.Property(e => e.AuthorizationCode)
                .HasMaxLength(50)
                .HasColumnName("Authorization Code");
            entity.Property(e => e.AuthorizationCurrency)
                .HasMaxLength(50)
                .HasColumnName("Authorization Currency");
            entity.Property(e => e.BOldSysTx).HasColumnName("bOldSysTx");
            entity.Property(e => e.BankAccountNumber)
                .HasMaxLength(50)
                .HasColumnName("Bank Account Number");
            entity.Property(e => e.BusinessDay)
                .HasMaxLength(50)
                .HasColumnName("Business Day");
            entity.Property(e => e.CardCodeStatus)
                .HasMaxLength(100)
                .HasColumnName("Card Code Status");
            entity.Property(e => e.CardNumber)
                .HasMaxLength(50)
                .HasColumnName("Card Number");
            entity.Property(e => e.CavvResultsCode)
                .HasMaxLength(50)
                .HasColumnName("CAVV Results Code");
            entity.Property(e => e.City).HasMaxLength(50);
            entity.Property(e => e.Company).HasMaxLength(50);
            entity.Property(e => e.Country).HasMaxLength(50);
            entity.Property(e => e.Currency).HasMaxLength(50);
            entity.Property(e => e.CustomerFirstName)
                .HasMaxLength(50)
                .HasColumnName("Customer First Name");
            entity.Property(e => e.CustomerId)
                .HasMaxLength(50)
                .HasColumnName("Customer ID");
            entity.Property(e => e.CustomerLastName)
                .HasMaxLength(50)
                .HasColumnName("Customer Last Name");
            entity.Property(e => e.Email).HasMaxLength(50);
            entity.Property(e => e.ExpirationDate)
                .HasMaxLength(50)
                .HasColumnName("Expiration Date");
            entity.Property(e => e.Fax).HasMaxLength(50);
            entity.Property(e => e.FraudscreenApplied)
                .HasMaxLength(50)
                .HasColumnName("Fraudscreen Applied");
            entity.Property(e => e.InvoiceDescription)
                .HasMaxLength(200)
                .HasColumnName("Invoice Description");
            entity.Property(e => e.InvoiceNumber)
                .HasMaxLength(50)
                .HasColumnName("Invoice Number");
            entity.Property(e => e.L2Duty)
                .HasMaxLength(50)
                .HasColumnName("L2 - Duty");
            entity.Property(e => e.L2Freight)
                .HasMaxLength(50)
                .HasColumnName("L2 - Freight");
            entity.Property(e => e.L2PurchaseOrderNumber)
                .HasMaxLength(50)
                .HasColumnName("L2 - Purchase Order Number");
            entity.Property(e => e.L2Tax)
                .HasMaxLength(50)
                .HasColumnName("L2 - Tax");
            entity.Property(e => e.L2TaxExempt)
                .HasMaxLength(50)
                .HasColumnName("L2 - Tax Exempt");
            entity.Property(e => e.PartialCaptureStatus)
                .HasMaxLength(50)
                .HasColumnName("Partial Capture Status");
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.RecurringBillingTransaction)
                .HasMaxLength(50)
                .HasColumnName("Recurring Billing Transaction");
            entity.Property(e => e.ReferenceTransactionId)
                .HasMaxLength(50)
                .HasColumnName("Reference Transaction ID");
            entity.Property(e => e.Reserved10).HasMaxLength(50);
            entity.Property(e => e.Reserved11).HasMaxLength(50);
            entity.Property(e => e.Reserved12).HasMaxLength(50);
            entity.Property(e => e.Reserved13).HasMaxLength(50);
            entity.Property(e => e.Reserved14).HasMaxLength(50);
            entity.Property(e => e.Reserved15).HasMaxLength(50);
            entity.Property(e => e.Reserved16).HasMaxLength(50);
            entity.Property(e => e.Reserved17).HasMaxLength(50);
            entity.Property(e => e.Reserved18).HasMaxLength(50);
            entity.Property(e => e.Reserved19).HasMaxLength(50);
            entity.Property(e => e.Reserved2).HasMaxLength(50);
            entity.Property(e => e.Reserved20).HasMaxLength(50);
            entity.Property(e => e.Reserved3).HasMaxLength(50);
            entity.Property(e => e.Reserved4).HasMaxLength(50);
            entity.Property(e => e.Reserved5).HasMaxLength(50);
            entity.Property(e => e.Reserved6).HasMaxLength(50);
            entity.Property(e => e.Reserved7).HasMaxLength(50);
            entity.Property(e => e.Reserved8).HasMaxLength(50);
            entity.Property(e => e.Reserved9).HasMaxLength(50);
            entity.Property(e => e.RoutingNumber)
                .HasMaxLength(50)
                .HasColumnName("Routing Number");
            entity.Property(e => e.SettlementAmount)
                .HasMaxLength(50)
                .HasColumnName("Settlement Amount");
            entity.Property(e => e.SettlementCurrency)
                .HasMaxLength(50)
                .HasColumnName("Settlement Currency");
            entity.Property(e => e.SettlementDateTime)
                .HasMaxLength(50)
                .HasColumnName("Settlement Date Time");
            entity.Property(e => e.ShipToAddress)
                .HasMaxLength(50)
                .HasColumnName("Ship-To Address");
            entity.Property(e => e.ShipToCity)
                .HasMaxLength(50)
                .HasColumnName("Ship-To City");
            entity.Property(e => e.ShipToCompany)
                .HasMaxLength(50)
                .HasColumnName("Ship-To Company");
            entity.Property(e => e.ShipToCountry)
                .HasMaxLength(50)
                .HasColumnName("Ship-To Country");
            entity.Property(e => e.ShipToFirstName)
                .HasMaxLength(50)
                .HasColumnName("Ship-To First Name");
            entity.Property(e => e.ShipToLastName)
                .HasMaxLength(50)
                .HasColumnName("Ship-To Last Name");
            entity.Property(e => e.ShipToState)
                .HasMaxLength(50)
                .HasColumnName("Ship-To State");
            entity.Property(e => e.ShipToZip)
                .HasMaxLength(50)
                .HasColumnName("Ship-To ZIP");
            entity.Property(e => e.State).HasMaxLength(50);
            entity.Property(e => e.SubmitDateTime)
                .HasMaxLength(50)
                .HasColumnName("Submit Date Time");
            entity.Property(e => e.TotalAmount)
                .HasMaxLength(50)
                .HasColumnName("Total Amount");
            entity.Property(e => e.TransactionStatus)
                .HasMaxLength(50)
                .HasColumnName("Transaction Status");
            entity.Property(e => e.TransactionType)
                .HasMaxLength(50)
                .HasColumnName("Transaction Type");
            entity.Property(e => e.Zip)
                .HasMaxLength(50)
                .HasColumnName("ZIP");
        });

        modelBuilder.Entity<VItemsToUpdate>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vItemsToUpdate", "stores");

            entity.Property(e => e.StoreItemName).IsUnicode(false);
            entity.Property(e => e.StoreItemPrice).HasColumnType("money");
        });

        modelBuilder.Entity<VMonthlyJobStats>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vMonthly_Job_Stats", "adn");

            entity.Property(e => e.Aid)
                .ValueGeneratedOnAdd()
                .HasColumnName("aid");
            entity.Property(e => e.CountActivePlayersToDate).HasColumnName("Count_ActivePlayersToDate");
            entity.Property(e => e.CountActivePlayersToDateLastMonth).HasColumnName("Count_ActivePlayersToDate_LastMonth");
            entity.Property(e => e.CountActiveTeamsToDate).HasColumnName("Count_ActiveTeamsToDate");
            entity.Property(e => e.CountActiveTeamsToDateLastMonth).HasColumnName("Count_ActiveTeamsToDate_LastMonth");
            entity.Property(e => e.CountNewPlayersThisMonth).HasColumnName("Count_NewPlayers_ThisMonth");
            entity.Property(e => e.CountNewTeamsThisMonth).HasColumnName("Count_NewTeams_ThisMonth");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.Modified)
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.Month).HasColumnName("month");
            entity.Property(e => e.Year).HasColumnName("year");
        });

        modelBuilder.Entity<VRegistrationsSearch>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vRegistrationsSearch", "Jobs");

            entity.Property(e => e.Assignment).HasColumnName("assignment");
            entity.Property(e => e.BActive).HasColumnName("bActive");
            entity.Property(e => e.Cellphone).HasColumnName("cellphone");
            entity.Property(e => e.Dob).HasColumnName("dob");
            entity.Property(e => e.OwedTotal)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("owed_total");
            entity.Property(e => e.PaidTotal)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("paid_total");
            entity.Property(e => e.Registrant).HasColumnName("registrant");
            entity.Property(e => e.RegistrationId).HasColumnName("RegistrationID");
            entity.Property(e => e.RegistrationTs).HasColumnName("RegistrationTS");
            entity.Property(e => e.RoleName).HasColumnName("role_name");
        });

        modelBuilder.Entity<VTeamCacreview>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vTeamCACReview", "utility");

            entity.Property(e => e.AgegroupName).HasColumnName("agegroupName");
            entity.Property(e => e.Effectiveasofdate).HasColumnName("effectiveasofdate");
            entity.Property(e => e.Enddate).HasColumnName("enddate");
            entity.Property(e => e.Expireondate).HasColumnName("expireondate");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.KeywordPairs)
                .IsUnicode(false)
                .HasColumnName("keywordPairs");
            entity.Property(e => e.PerRegistrantFee)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("perRegistrantFee");
            entity.Property(e => e.Startdate).HasColumnName("startdate");
            entity.Property(e => e.TeamComments).HasColumnName("team_comments");
            entity.Property(e => e.TeamName).HasColumnName("teamName");
        });

        modelBuilder.Entity<VTxs>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vTxs", "adn");

            entity.Property(e => e.AuthorizationAmount)
                .HasMaxLength(50)
                .HasColumnName("Authorization Amount");
            entity.Property(e => e.BOldSysTx).HasColumnName("bOldSysTx");
            entity.Property(e => e.CustomerId).HasColumnName("customerID");
            entity.Property(e => e.CustomerName).HasColumnName("customerName");
            entity.Property(e => e.InvoiceNumber)
                .HasMaxLength(50)
                .HasColumnName("Invoice Number");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.JobName).HasColumnName("jobName");
            entity.Property(e => e.PaymentMethod).HasColumnName("paymentMethod");
            entity.Property(e => e.ReferenceTransactionId)
                .HasMaxLength(50)
                .HasColumnName("Reference Transaction ID");
            entity.Property(e => e.RegistrantFirstName).HasColumnName("Registrant_FirstName");
            entity.Property(e => e.RegistrantLastName).HasColumnName("Registrant_LastName");
            entity.Property(e => e.SettlementAmount)
                .HasColumnType("money")
                .HasColumnName("Settlement Amount");
            entity.Property(e => e.SettlementDateTime)
                .HasMaxLength(50)
                .HasColumnName("Settlement Date Time");
            entity.Property(e => e.SettlementTs)
                .HasColumnType("datetime")
                .HasColumnName("SettlementTS");
            entity.Property(e => e.TransactionId)
                .HasMaxLength(50)
                .HasColumnName("Transaction ID");
            entity.Property(e => e.TransactionStatus)
                .HasMaxLength(50)
                .HasColumnName("Transaction Status");
            entity.Property(e => e.TransactionType)
                .HasMaxLength(50)
                .HasColumnName("Transaction Type");
        });

        modelBuilder.Entity<VerticalInsurePayouts>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Vertical__3214EC07C901FFF8");

            entity.ToTable("Vertical-Insure-Payouts");

            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.NetWrittenPremium)
                .HasColumnType("money")
                .HasColumnName("Net Written Premium");
            entity.Property(e => e.Payout).HasColumnType("money");
            entity.Property(e => e.PolicyEffectiveDate)
                .HasColumnType("datetime")
                .HasColumnName("Policy Effective Date");
            entity.Property(e => e.PolicyNumber)
                .HasMaxLength(255)
                .HasColumnName("Policy Number");
            entity.Property(e => e.PurchaseDate).HasColumnType("datetime");
            entity.Property(e => e.PurchaseDateString).HasMaxLength(255);
        });

        modelBuilder.Entity<Widget>(entity =>
        {
            entity.HasKey(e => e.WidgetId).HasName("PK_widgets_Widget");

            entity.ToTable("Widget", "widgets");

            entity.Property(e => e.ComponentKey).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.WidgetType).HasMaxLength(30);

            entity.HasOne(d => d.Category).WithMany(p => p.Widget)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_widgets_Widget_CategoryId");
        });

        modelBuilder.Entity<WidgetCategory>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK_widgets_WidgetCategory");

            entity.ToTable("WidgetCategory", "widgets");

            entity.Property(e => e.Icon).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Workspace).HasMaxLength(20);
        });

        modelBuilder.Entity<WidgetDefault>(entity =>
        {
            entity.HasKey(e => e.WidgetDefaultId).HasName("PK_widgets_WidgetDefault");

            entity.ToTable("WidgetDefault", "widgets");

            entity.HasIndex(e => new { e.JobTypeId, e.RoleId, e.WidgetId, e.CategoryId }, "UQ_widgets_WidgetDefault_JobType_Role_Widget_Category").IsUnique();

            entity.HasOne(d => d.Category).WithMany(p => p.WidgetDefault)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_widgets_WidgetDefault_CategoryId");

            entity.HasOne(d => d.JobType).WithMany(p => p.WidgetDefault)
                .HasForeignKey(d => d.JobTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_widgets_WidgetDefault_JobTypeId");

            entity.HasOne(d => d.Role).WithMany(p => p.WidgetDefault)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_widgets_WidgetDefault_RoleId");

            entity.HasOne(d => d.Widget).WithMany(p => p.WidgetDefault)
                .HasForeignKey(d => d.WidgetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_widgets_WidgetDefault_WidgetId");
        });

        modelBuilder.Entity<Yn2023schedule>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("YN2023Schedule");

            entity.Property(e => e.AgegroupId).HasColumnName("agegroupID");
            entity.Property(e => e.AgegroupName).HasColumnName("agegroupName");
            entity.Property(e => e.Div2Id).HasColumnName("div2ID");
            entity.Property(e => e.Div2Name).HasColumnName("div2Name");
            entity.Property(e => e.DivId).HasColumnName("divID");
            entity.Property(e => e.DivName).HasColumnName("divName");
            entity.Property(e => e.FName).HasColumnName("fName");
            entity.Property(e => e.FieldId).HasColumnName("fieldID");
            entity.Property(e => e.GDate)
                .HasColumnType("datetime")
                .HasColumnName("G_Date");
            entity.Property(e => e.GNo).HasColumnName("G_No");
            entity.Property(e => e.GStatusCode).HasColumnName("g_statusCode");
            entity.Property(e => e.Gid)
                .ValueGeneratedOnAdd()
                .HasColumnName("GID");
            entity.Property(e => e.JobId).HasColumnName("jobID");
            entity.Property(e => e.LeagueId).HasColumnName("leagueID");
            entity.Property(e => e.LeagueName).HasColumnName("leagueName");
            entity.Property(e => e.LebUserId)
                .HasMaxLength(450)
                .HasColumnName("lebUserId");
            entity.Property(e => e.Modified)
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.RefCount)
                .HasColumnType("decimal(2, 1)")
                .HasColumnName("ref_count");
            entity.Property(e => e.RescheduleCount).HasColumnName("rescheduleCount");
            entity.Property(e => e.Rnd).HasColumnName("rnd");
            entity.Property(e => e.Season)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.T1Ann)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("t1_ann");
            entity.Property(e => e.T1CalcType)
                .IsUnicode(false)
                .HasColumnName("T1_CalcType");
            entity.Property(e => e.T1GnoRef).HasColumnName("T1_GnoRef");
            entity.Property(e => e.T1Id).HasColumnName("T1_ID");
            entity.Property(e => e.T1Name)
                .IsUnicode(false)
                .HasColumnName("T1_Name");
            entity.Property(e => e.T1No).HasColumnName("T1_No");
            entity.Property(e => e.T1Score).HasColumnName("T1_Score");
            entity.Property(e => e.T1Type)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("T1_Type");
            entity.Property(e => e.T1penalties).HasColumnName("t1penalties");
            entity.Property(e => e.T2Ann)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("t2_ann");
            entity.Property(e => e.T2CalcType)
                .IsUnicode(false)
                .HasColumnName("T2_CalcType");
            entity.Property(e => e.T2GnoRef).HasColumnName("T2_GNoRef");
            entity.Property(e => e.T2Id).HasColumnName("T2_ID");
            entity.Property(e => e.T2Name)
                .IsUnicode(false)
                .HasColumnName("T2_Name");
            entity.Property(e => e.T2No).HasColumnName("T2_No");
            entity.Property(e => e.T2Score).HasColumnName("T2_Score");
            entity.Property(e => e.T2Type)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("T2_Type");
            entity.Property(e => e.T2penalties).HasColumnName("t2penalties");
            entity.Property(e => e.Year)
                .HasMaxLength(4)
                .IsUnicode(false)
                .IsFixedLength();
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
