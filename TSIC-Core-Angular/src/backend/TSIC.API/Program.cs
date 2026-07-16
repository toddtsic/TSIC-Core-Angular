using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Data.Identity;
using TSIC.Infrastructure.Repositories;
using TSIC.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using TSIC.Application.Services.Auth;
using TSIC.Application.Services.Users;
using TSIC.Application.Services.Shared.Html;
using TSIC.Application.Services.Shared.Text;
using TSIC.Application.Services.Shared.Discount;
using TSIC.Application.Validators;
using TSIC.Infrastructure.Services.Auth;
using TSIC.Infrastructure.Services.Users;
using TSIC.Infrastructure.Services;
using TSIC.Contracts.Services;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Repositories;

using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Adults;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Configuration;
using TSIC.API.Services.Shared.Files;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Shared.UsLax;
using TSIC.API.Services.Shared.Email;
using TSIC.API.Services.Shared.Registration;
using TSIC.Application.Services.DiscountCode;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Scheduling;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Shared.Utilities;
using TSIC.API.Services.Shared.AiCompose;
using TSIC.API.Services.Shared.Bulletins;
using TSIC.API.Services.Shared.Bulletins.TokenResolution;
using TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;
using TSIC.API.Services.Shared.Devices;
using TSIC.API.Services.Shared.Firebase;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Sweep;
using TSIC.API.Services.Reporting;
using TSIC.API.Services;
using TSIC.API.Services.Referees;
using TSIC.API.Services.Store;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Widgets;
using TSIC.API.Authorization;
using Amazon.SimpleEmail;
using Amazon.Runtime;
using Amazon;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── ASPNETCORE_ENVIRONMENT must be set explicitly ───────────────────
// ASP.NET silently defaults to "Production" when the env var is missing. That
// default would load the prod overlay (and live external creds) on any box
// where deployment forgot to set the var. Refuse to start instead — the
// deployment is the single source of truth for which environment this host is.
{
    var rawEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    if (string.IsNullOrWhiteSpace(rawEnv))
    {
        throw new InvalidOperationException(
            "ASPNETCORE_ENVIRONMENT must be set explicitly. Refusing to fall back to ASP.NET's default 'Production'.");
    }

    Console.WriteLine($"[startup] env={builder.Environment.EnvironmentName} machine={System.Environment.MachineName}");
}

// ── Syncfusion license registration ─────────────────────────────────
// Unlocks Syncfusion.Pdf (and XlsIO once it replaces EPPlus) for production use.
// Not a real secret: the Angular bundle's registerLicense() already ships the same
// key publicly, so it lives in committed appsettings.json ("Syncfusion:LicenseKey")
// and travels with the repo. An env var (Syncfusion__LicenseKey) still overrides it
// if ever needed. When unset, registration is skipped so Syncfusion runs in trial
// mode instead of throwing on a null key.
{
    var syncfusionLicenseKey = builder.Configuration["Syncfusion:LicenseKey"];
    if (!string.IsNullOrWhiteSpace(syncfusionLicenseKey))
    {
        Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionLicenseKey);
    }
}

// ── Bold Reports license registration ──────────────────────────────
// Unlocks BoldReports.Net.Core (Community / Enterprise) for server-side RDL
// rendering. Lives in the Bold.Licensing assembly — NOT BoldReports.Web — and
// must run before any ReportWriter is instantiated, hence its position before
// service registration. Key is a real secret: app pool env var
// (BoldReports__LicenseKey) in IIS, user-secrets in local Development.
{
    var boldLicenseKey = builder.Configuration["BoldReports:LicenseKey"];
    if (!string.IsNullOrWhiteSpace(boldLicenseKey))
    {
        Bold.Licensing.BoldLicenseProvider.RegisterLicense(boldLicenseKey);
    }
}

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddMemoryCache(); // Add memory cache for refresh tokens

// Response compression (gzip + brotli for JSON API responses)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes;
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = System.IO.Compression.CompressionLevel.SmallestSize);

// Infrastructure Repositories
builder.Services.AddScoped<IRegistrationRepository, RegistrationRepository>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITeamRepository, TeamRepository>();
builder.Services.AddScoped<IAgeGroupRepository, AgeGroupRepository>();
builder.Services.AddScoped<IAgeRangeRepository, AgeRangeRepository>();
builder.Services.AddScoped<IFamilyRepository, FamilyRepository>();
builder.Services.AddScoped<IJobDiscountCodeRepository, JobDiscountCodeRepository>();
builder.Services.AddScoped<IClubRepRepository, ClubRepRepository>();
builder.Services.AddScoped<IJobLeagueRepository, JobLeagueRepository>();
builder.Services.AddScoped<IClubRepository, ClubRepository>();
builder.Services.AddScoped<IClubTeamRepository, ClubTeamRepository>();
builder.Services.AddScoped<IFamiliesRepository, FamiliesRepository>();
builder.Services.AddScoped<IFamilyMemberRepository, FamilyMemberRepository>();
builder.Services.AddScoped<IRegistrationAccountingRepository, RegistrationAccountingRepository>();
builder.Services.AddScoped<IBulletinRepository, BulletinRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<ICustomerGroupRepository, CustomerGroupRepository>();
builder.Services.AddScoped<ITextSubstitutionRepository, TextSubstitutionRepository>();
builder.Services.AddScoped<IProfileMetadataRepository, ProfileMetadataRepository>();
builder.Services.AddScoped<IAdultRegistrationRepository, AdultRegistrationRepository>();
builder.Services.AddScoped<IAdministratorRepository, AdministratorRepository>();
builder.Services.AddScoped<IReportingRepository, ReportingRepository>();
builder.Services.AddScoped<ILeagueRepository, LeagueRepository>();
builder.Services.AddScoped<IDivisionRepository, DivisionRepository>();
builder.Services.AddScoped<IScheduleCascadeRepository, ScheduleCascadeRepository>();
builder.Services.AddScoped<IScheduleRepository, ScheduleRepository>();
builder.Services.AddScoped<IJobFilterTreeRepository, JobFilterTreeRepository>();
builder.Services.AddScoped<IAutoBuildRepository, AutoBuildRepository>();
builder.Services.AddScoped<IBracketSeedRepository, BracketSeedRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IFieldRepository, FieldRepository>();
builder.Services.AddScoped<IPairingsRepository, PairingsRepository>();
builder.Services.AddScoped<IBracketRepository, BracketRepository>();
builder.Services.AddScoped<ITimeslotRepository, TimeslotRepository>();
builder.Services.AddScoped<ITournamentParkingRepository, TournamentParkingRepository>();
builder.Services.AddScoped<IUserWidgetRepository, UserWidgetRepository>();
builder.Services.AddScoped<IWidgetRepository, WidgetRepository>();
builder.Services.AddScoped<IWidgetEditorRepository, WidgetEditorRepository>();
builder.Services.AddScoped<IJobCloneRepository, JobCloneRepository>();
builder.Services.AddScoped<IDdlOptionsRepository, DdlOptionsRepository>();
builder.Services.AddScoped<IEmailLogRepository, EmailLogRepository>();
builder.Services.AddScoped<IMobileScorerRepository, MobileScorerRepository>();
builder.Services.AddScoped<IArbSubscriptionRepository, ArbSubscriptionRepository>();

builder.Services.AddScoped<IVisibilityRulesEvaluator, VisibilityRulesEvaluator>();
builder.Services.AddScoped<INavRepository, NavRepository>();
builder.Services.AddScoped<INavEditorRepository, NavEditorRepository>();
builder.Services.AddScoped<IJobConfigRepository, JobConfigRepository>();
// Referee Assignment
builder.Services.AddScoped<IRefAssignmentRepository, RefAssignmentRepository>();
// Store
builder.Services.AddScoped<IStoreAnalyticsRepository, StoreAnalyticsRepository>();
builder.Services.AddScoped<IStoreCartRepository, StoreCartRepository>();
builder.Services.AddScoped<IStoreItemRepository, StoreItemRepository>();
builder.Services.AddScoped<IStoreRepository, StoreRepository>();
builder.Services.AddScoped<IChangePasswordRepository, ChangePasswordRepository>();
builder.Services.AddScoped<ICustomerJobRevenueRepository, CustomerJobRevenueRepository>();
builder.Services.AddScoped<IPushNotificationRepository, PushNotificationRepository>();
builder.Services.AddScoped<ITeamLinkRepository, TeamLinkRepository>();
// Fees
builder.Services.AddScoped<IFeeRepository, FeeRepository>();
// Live check-in (staff station)
builder.Services.AddScoped<ICheckinRepository, CheckinRepository>();

// FileStorage configuration + Image service
builder.Services.Configure<FileStorageOptions>(
    builder.Configuration.GetSection(FileStorageOptions.SectionName));
builder.Services.AddScoped<IJobImageService, JobImageService>();
builder.Services.AddScoped<IMedFormService, MedFormService>();

// TsicSettings (default customer for ADN credential defaults)
builder.Services.Configure<TsicSettings>(
    builder.Configuration.GetSection(TsicSettings.SectionName));

// Anthropic AI Compose (email drafting via Claude Haiku)
builder.Services.Configure<AnthropicSettings>(
    builder.Configuration.GetSection(AnthropicSettings.SectionName));
builder.Services.AddHttpClient<IAiComposeService, AiComposeService>();

// Application & Infrastructure Services
builder.Services.AddScoped<IMenuRepository, MenuRepository>();
builder.Services.AddScoped<INavEditorService, NavEditorService>();
builder.Services.AddScoped<TSIC.Application.Services.MenuAdmin.IMenuAdminService, TSIC.Application.Services.MenuAdmin.MenuAdminService>();
builder.Services.AddScoped<IRoleLookupService, RoleLookupService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IJobLookupService, JobLookupService>();
builder.Services.AddScoped<ITeamLookupService, TeamLookupService>();
builder.Services.AddScoped<IAdnApiService, AdnApiService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IPlayerRegistrationService, PlayerRegistrationService>();
builder.Services.AddScoped<IPlayerFormValidationService, PlayerFormValidationService>();
builder.Services.AddScoped<IFeeResolutionService, FeeResolutionService>();
builder.Services.AddScoped<IPlayerRegistrationMetadataService, PlayerRegistrationMetadataService>();
builder.Services.AddScoped<IRegistrationFeeAdjustmentService, RegistrationFeeAdjustmentService>();
builder.Services.AddScoped<IPaymentStateService, PaymentStateService>();
builder.Services.AddScoped<IVerticalInsureService, VerticalInsureService>();
builder.Services.AddScoped<IDiscountCodeEvaluator, DiscountCodeEvaluatorService>();
builder.Services.AddScoped<ITextSubstitutionService, TextSubstitutionService>();
builder.Services.AddScoped<IBulletinService, BulletinService>();

// Bulletin !TOKEN resolvers — each registered as IBulletinTokenResolver; BulletinTokenRegistry collects them.
builder.Services.AddScoped<IBulletinTokenResolver, RegisterPlayerResolver>();
builder.Services.AddScoped<IBulletinTokenResolver, RegisterClubRepResolver>();
builder.Services.AddScoped<IBulletinTokenResolver, RegisterUnassignedAdultResolver>();
builder.Services.AddScoped<IBulletinTokenResolver, RegisterStaffResolver>();
builder.Services.AddScoped<IBulletinTokenResolver, RegisterSelfRosterPlayersAndCoachResolver>();
builder.Services.AddScoped<IBulletinTokenResolver, PublicRostersResolver>();
builder.Services.AddScoped<IBulletinTokenResolver, ScheduleResolver>();
builder.Services.AddScoped<IBulletinTokenResolver, EventInfoResolver>();
builder.Services.AddScoped<BulletinTokenRegistry>();
builder.Services.AddScoped<IAgeRangeService, AgeRangeService>();
builder.Services.AddScoped<IPlayerRegConfirmationService, PlayerRegConfirmationService>();
// VerticalInsure named HttpClient registration (base address only; secrets via env vars VI_DEV_SECRET/VI_PROD_SECRET)
builder.Services.AddHttpClient("verticalinsure", (sp, c) =>
{
    // Prefer configuration or env override to satisfy analyzer (S1075)
    var cfgBase = builder.Configuration.GetValue<string>("VerticalInsure:BaseUrl")
                  ?? Environment.GetEnvironmentVariable("VI_BASE_URL")
                  ?? "https://api.verticalinsure.com"; // fallback
    c.BaseAddress = new Uri(cfgBase);
    c.DefaultRequestHeaders.Add("User-Agent", "TSIC.API");
});
builder.Services.AddScoped<IFamilyService, FamilyService>();
builder.Services.AddScoped<IUserProfileService, TSIC.API.Services.Account.UserProfileService>();
builder.Services.AddScoped<IClubService, ClubService>();
builder.Services.AddScoped<IClubRosterService, ClubRosterService>();
builder.Services.AddScoped<IMyRosterService, TSIC.API.Services.MyRoster.MyRosterService>();
builder.Services.AddScoped<IUserPrivilegeLevelService, UserPrivilegeLevelService>();
builder.Services.AddScoped<TSIC.API.Services.SuggestedEvents.ISuggestedEventsService, TSIC.API.Services.SuggestedEvents.SuggestedEventsService>();
builder.Services.AddScoped<ITeamPlacementService, TeamPlacementService>();
builder.Services.AddScoped<TSIC.API.Services.Teams.IRegisteredTeamShaper, TSIC.API.Services.Teams.RegisteredTeamShaper>();
builder.Services.AddScoped<TSIC.API.Services.Players.IRegisteredPlayerShaper, TSIC.API.Services.Players.RegisteredPlayerShaper>();
builder.Services.AddScoped<ITeamRegistrationService, TeamRegistrationService>();
builder.Services.AddScoped<IProfileMetadataService, ProfileMetadataService>();
builder.Services.AddScoped<IRegistrationQueryService, RegistrationQueryService>();
builder.Services.AddScoped<IUsLaxService, UsLaxService>();
builder.Services.AddScoped<IUsLaxIdentityVerificationService, UsLaxIdentityVerificationService>();
builder.Services.AddScoped<IUsLaxMembershipService, UsLaxMembershipService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<TSIC.API.Services.Invites.IInviteTokenService, TSIC.API.Services.Invites.InviteTokenService>();
builder.Services.AddScoped<IAdultRegistrationService, AdultRegistrationService>();
builder.Services.AddScoped<IAdministratorService, AdministratorService>();
builder.Services.AddScoped<ICustomerGroupService, CustomerGroupService>();
builder.Services.AddScoped<IDiscountCodeService, DiscountCodeService>();
builder.Services.AddScoped<ILadtService, LadtService>();
builder.Services.AddScoped<IRosterSwapperService, RosterSwapperService>();
builder.Services.AddScoped<IPoolAssignmentService, PoolAssignmentService>();
builder.Services.AddScoped<IRegistrationSearchService, RegistrationSearchService>();
builder.Services.AddScoped<ITeamSearchService, TeamSearchService>();
// Scheduling
builder.Services.AddScoped<ISchedulingContextResolver, SchedulingContextResolver>();
builder.Services.AddScoped<IFieldManagementService, FieldManagementService>();
builder.Services.AddScoped<IPairingsService, PairingsService>();
builder.Services.AddScoped<ITimeslotService, TimeslotService>();
builder.Services.AddScoped<IScheduleDivisionService, ScheduleDivisionService>();
builder.Services.AddScoped<IAutoBuildScheduleService, AutoBuildScheduleService>();
builder.Services.AddScoped<IBracketGenerationService, BracketGenerationService>();
builder.Services.AddScoped<IBracketAdvancementService, BracketAdvancementService>();
builder.Services.AddScoped<IBracketSeedResolutionService, BracketSeedResolutionService>();
builder.Services.AddScoped<IBracketDevToolsService, BracketDevToolsService>();
builder.Services.AddScoped<IBracketSeedService, BracketSeedService>();
builder.Services.AddScoped<IScheduleCascadeService, ScheduleCascadeService>();
builder.Services.AddScoped<IScheduleQaService, ScheduleQaService>();
builder.Services.AddScoped<IViewScheduleService, ViewScheduleService>();
builder.Services.AddScoped<IMasterScheduleService, MasterScheduleService>();
builder.Services.AddScoped<IReschedulerService, ReschedulerService>();
builder.Services.AddScoped<ISchedulingDashboardService, SchedulingDashboardService>();
builder.Services.AddScoped<ITournamentParkingService, TournamentParkingService>();
// Widget Dashboard
builder.Services.AddScoped<IUserWidgetService, UserWidgetService>();
builder.Services.AddScoped<IWidgetDashboardService, WidgetDashboardService>();
builder.Services.AddScoped<IWidgetEditorService, WidgetEditorService>();
builder.Services.AddScoped<IJobCloneService, JobCloneService>();
builder.Services.AddScoped<IDdlOptionsService, DdlOptionsService>();
builder.Services.AddScoped<IJobConfigService, JobConfigService>();
builder.Services.AddScoped<IJobVisibilityService, JobVisibilityService>();
// The single authority for registration-CREATE permission (door · toggle · precondition).
builder.Services.AddScoped<IJobRegistrationCapabilities, JobRegistrationCapabilities>();
builder.Services.AddScoped<IJobPaymentFeaturesService, JobPaymentFeaturesService>();
// ARB Defensive
builder.Services.AddScoped<IArbDefensiveService, ArbDefensiveService>();
// Customer Configure
builder.Services.AddScoped<ICustomerConfigureService, CustomerConfigureService>();
// Mobile Scorers
builder.Services.AddScoped<IMobileScorerService, MobileScorerService>();
// Referee Assignment
builder.Services.AddScoped<IRefAssignmentService, RefAssignmentService>();
// US Lacrosse Rankings
builder.Services.AddHttpClient<IUSLaxScrapingService, TSIC.Infrastructure.Services.USLaxScrapingService>();
builder.Services.AddScoped<IUSLaxMatchingService, TSIC.API.Services.Rankings.USLaxMatchingService>();
// Store
builder.Services.AddScoped<IStoreAdminService, StoreAdminService>();
builder.Services.AddScoped<IStoreCatalogService, StoreCatalogService>();
builder.Services.AddScoped<IStoreCartService, StoreCartService>();
builder.Services.AddScoped<IStoreWalkUpService, StoreWalkUpService>();
builder.Services.AddScoped<IStoreReceiptService, StoreReceiptService>();
// Change Password (SuperUser admin utility)
builder.Services.AddScoped<IChangePasswordService, ChangePasswordService>();
// Push Notifications (Admin — Firebase Cloud Messaging)
builder.Services.AddSingleton<IFirebasePushService, FirebasePushService>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddScoped<IGameResultPushService, GameResultPushService>();
// Team Links (Admin — communications/team-links page; legacy MobileTeamLinks port)
builder.Services.AddScoped<ITeamLinkService, TeamLinkService>();
// Mobile API — Device Management, Event Browse, Team Management
builder.Services.AddScoped<IDeviceManagementService, DeviceManagementService>();
builder.Services.AddScoped<IEventBrowseService, EventBrowseService>();
builder.Services.AddScoped<ITeamDocsRepository, TeamDocsRepository>();
builder.Services.AddScoped<ITeamManagementService, TeamManagementService>();
builder.Services.AddScoped<ITeamAttendanceRepository, TeamAttendanceRepository>();
builder.Services.AddScoped<ITeamAttendanceService, TeamAttendanceService>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IFileUploadService, TSIC.API.Services.Shared.Files.FileUploadService>();
builder.Services.AddSignalR();
// Uniform Number Upload (admin bulk update)
builder.Services.AddScoped<IUniformUploadService, UniformUploadService>();
// Camp Day/Night Groups admin
builder.Services.AddScoped<ICampGroupsService, CampGroupsService>();
// Live check-in (staff station)
builder.Services.AddScoped<ICheckinService, CheckinService>();
// RegSaver Monthly Payouts Upload (SuperUser cross-customer ingest)
builder.Services.AddScoped<IVerticalInsurePayoutsRepository, VerticalInsurePayoutsRepository>();
builder.Services.AddScoped<IRegSaverUploadService, RegSaverUploadService>();
// Nuvei Monthly Funding/Batches Upload (SuperUser cross-customer ingest)
builder.Services.AddScoped<INuveiBatchesRepository, NuveiBatchesRepository>();
builder.Services.AddScoped<INuveiFundingRepository, NuveiFundingRepository>();
builder.Services.AddScoped<INuveiUploadService, NuveiUploadService>();
// ADN Monthly Reconciliation (SuperUser; pulls prod ADN by design — see project memory)
builder.Services.AddScoped<IAdnReconciliationRepository, AdnReconciliationRepository>();
builder.Services.AddScoped<IAdnReconciliationService, AdnReconciliationService>();
// Last Months Job Stats (SuperUser cross-customer review/edit grid)
builder.Services.AddScoped<ILastMonthsJobStatsRepository, LastMonthsJobStatsRepository>();
builder.Services.AddScoped<ILastMonthsJobStatsService, LastMonthsJobStatsService>();
// Customer Job Revenue (SuperUser financial dashboard)
builder.Services.AddScoped<ICustomerJobRevenueService, CustomerJobRevenueService>();
// Reporting
builder.Services.Configure<ReportingSettings>(builder.Configuration.GetSection("Reporting"));
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IPackedRosterPdfService, PackedRosterPdfService>();
builder.Services.AddScoped<IScheduleListReportService, ScheduleListReportService>();
builder.Services.AddScoped<IRosterTablePdfService, RosterTablePdfService>();
builder.Services.AddScoped<IMyRosterPdfService, MyRosterPdfService>();
builder.Services.AddScoped<IDailyRegCountsPdfService, DailyRegCountsPdfService>();
builder.Services.AddScoped<IInvoiceReportPdfService, InvoiceReportPdfService>();
builder.Services.AddScoped<IFeeYtdReportPdfService, FeeYtdReportPdfService>();
builder.Services.AddScoped<IPlayerStatsReportPdfService, PlayerStatsReportPdfService>();
builder.Services.AddScoped<IAmericanSelectReportPdfService, AmericanSelectReportPdfService>();
builder.Services.AddScoped<IGameBoardsPdfService, GameBoardsPdfService>();
builder.Services.AddScoped<IShowcaseScheduleReportService, ShowcaseScheduleReportService>();
builder.Services.AddScoped<IClubRosterPdfService, ClubRosterPdfService>();
builder.Services.AddHttpClient("CrystalReports");
// Email (Amazon SES only)
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.Configure<AwsSettings>(builder.Configuration.GetSection("AWS"));
builder.Services.AddSingleton<IAmazonSimpleEmailService>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<EmailSettings>>().Value;
    var aws = sp.GetRequiredService<IOptions<AwsSettings>>().Value;

    // Region priority: EmailSettings.AwsRegion -> AwsSettings.Region -> ENV (AWS_REGION/AWS_DEFAULT_REGION) -> SDK default chain
    var regionName = opts.AwsRegion
                     ?? aws.Region
                     ?? Environment.GetEnvironmentVariable("AWS_REGION")
                     ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
    var region = !string.IsNullOrWhiteSpace(regionName) ? Amazon.RegionEndpoint.GetBySystemName(regionName) : null;

    // Credentials priority: secrets (AwsSettings) -> SDK default chain
    var haveKeys = !string.IsNullOrWhiteSpace(aws.AccessKey) && !string.IsNullOrWhiteSpace(aws.SecretKey);

    if (haveKeys && region != null)
    {
        return new AmazonSimpleEmailServiceClient(new BasicAWSCredentials(aws.AccessKey!, aws.SecretKey!), region);
    }
    if (haveKeys)
    {
        return new AmazonSimpleEmailServiceClient(new BasicAWSCredentials(aws.AccessKey!, aws.SecretKey!));
    }
    if (region != null)
    {
        return new AmazonSimpleEmailServiceClient(region);
    }
    return new AmazonSimpleEmailServiceClient();
});
// Amazon SES v2 client — same region/credential cascade as the v1 client above.
// Needed only for the suppression-list APIs (GetSuppressedDestination/DeleteSuppressedDestination)
// which the E-Mail Troubleshooter uses; the v1 client remains the send transport.
builder.Services.AddSingleton<Amazon.SimpleEmailV2.IAmazonSimpleEmailServiceV2>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<EmailSettings>>().Value;
    var aws = sp.GetRequiredService<IOptions<AwsSettings>>().Value;

    var regionName = opts.AwsRegion
                     ?? aws.Region
                     ?? Environment.GetEnvironmentVariable("AWS_REGION")
                     ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
    var region = !string.IsNullOrWhiteSpace(regionName) ? Amazon.RegionEndpoint.GetBySystemName(regionName) : null;

    var haveKeys = !string.IsNullOrWhiteSpace(aws.AccessKey) && !string.IsNullOrWhiteSpace(aws.SecretKey);

    if (haveKeys && region != null)
    {
        return new Amazon.SimpleEmailV2.AmazonSimpleEmailServiceV2Client(new BasicAWSCredentials(aws.AccessKey!, aws.SecretKey!), region);
    }
    if (haveKeys)
    {
        return new Amazon.SimpleEmailV2.AmazonSimpleEmailServiceV2Client(new BasicAWSCredentials(aws.AccessKey!, aws.SecretKey!));
    }
    if (region != null)
    {
        return new Amazon.SimpleEmailV2.AmazonSimpleEmailServiceV2Client(region);
    }
    return new Amazon.SimpleEmailV2.AmazonSimpleEmailServiceV2Client();
});
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddScoped<IEmailTroubleshooterService, EmailTroubleshooterService>();
// Batch-email engine: background orchestration above the SES transport. Both singletons
// (engine owns no scoped state; render workers create their own scopes per IEmailBatchService).
builder.Services.AddSingleton<IEmailBatchJobRegistry, EmailBatchJobRegistry>();
builder.Services.AddSingleton<IEmailBatchService, EmailBatchService>();

// US LAX settings and HTTP client
builder.Services.Configure<UsLaxSettings>(builder.Configuration.GetSection("UsLax"));
builder.Services.AddHttpClient("uslax", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<UsLaxSettings>>().Value;
    var baseUrl = opts.ApiBase ?? Environment.GetEnvironmentVariable("USLAX_API_BASE") ?? "https://api.usalacrosse.com/";
    client.BaseAddress = new Uri(baseUrl);
});

// VerticalInsure settings
builder.Services.Configure<VerticalInsureSettings>(builder.Configuration.GetSection("VerticalInsure"));

// Authorize.Net settings (sandbox credentials only - production comes from database)
builder.Services.Configure<AdnSettings>(builder.Configuration.GetSection("AuthorizeNet"));

// Daily ADN reconciliation sweep (ARB import + eCheck return processing)
builder.Services.Configure<AdnSweepOptions>(builder.Configuration.GetSection("AdnSweep"));
builder.Services.AddScoped<IEcheckSettlementRepository, EcheckSettlementRepository>();
builder.Services.AddScoped<IAdnSweepService, AdnSweepService>();
builder.Services.AddHostedService<AdnSweepBackgroundService>();

// Profile Migration Services
builder.Services.AddScoped<IGitHubProfileFetcher, GitHubProfileFetcher>();
builder.Services.AddScoped<CSharpToMetadataParser>();
builder.Services.AddScoped<ProfileMetadataMigrationService>();
// Interface handle forwards to the same scoped instance so consumers depending on the abstraction
// (e.g. JobConfigService.ComputeCoachFormSwap) share the concrete the ProfileMigrationController uses.
builder.Services.AddScoped<IProfileMetadataMigrationService>(sp => sp.GetRequiredService<ProfileMetadataMigrationService>());

// Add FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

// Only register SqlServer DbContexts if not running tests
// Tests will provide in-memory versions via WebApplicationTestFactory
if (!builder.Environment.IsEnvironment("Testing"))
{
    // FeeTotal/OwedTotal are derived money written solely by RecalcTotals (TSIC.Contracts),
    // so the Stage-A observe/shadow interceptor was removed as redundant.
    builder.Services.AddDbContext<SqlDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            x => x.UseNetTopologySuite()));

    // Separate DbContext for Identity operations only
    builder.Services.AddDbContext<TsicIdentityDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
}

//PASSWORD RESTRICTIONS
builder.Services.Configure<IdentityOptions>(options =>
{
    // Password settings
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
});
// Configure Identity to use the dedicated TsicIdentityDbContext with ApplicationUser
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-!._@+/ ")
    .AddEntityFrameworkStores<TsicIdentityDbContext>()
    .AddDefaultTokenProviders();

// Password reset token lifetime (1 hour)
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(1));

// The key ring that seals the token above MUST outlive a restart, or the 1-hour lifespan is a lie.
//
// Under IIS the app pool has no user profile, so Data Protection's default discovery finds nowhere
// to persist keys, falls back to an in-memory ring, and says so only in a startup warning. That ring
// is regenerated on every pool recycle -- i.e. on every deploy, and on every IIS idle timeout. Each
// recycle silently invalidates every password-reset link already sitting in a user's inbox: they
// click it and get "Invalid or expired reset link", which is indistinguishable from a real expiry,
// so it never gets reported as a bug. (AuthController.ForgotPassword mints the token and emails the
// link; AuthController.ResetPassword validates it in a LATER request, which is why the key has to
// survive in between. The admin reset in ChangePasswordService is unaffected -- it mints and
// consumes the token in one request, so nothing has to persist.)
//
// keys\ sits beside the app, is granted Modify to the pool by IIS-Config-{Dev,Prod}/Setup/
// 03-Create-Directories.ps1, and is excluded from every deploy, backup and rollback copy by
// scripts/_deploy-common.ps1 -- so the ring survives a deploy and a restore.
//
// Development is deliberately left on the framework default: dotnet run has a user profile and
// already persists to %LOCALAPPDATA%\ASP.NET\DataProtection-Keys, and here ContentRoot IS the source
// tree -- pointing it at ContentRoot/keys would drop private key XML into the repo.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(
            Path.Combine(builder.Environment.ContentRootPath, "keys")))
        .SetApplicationName("TSIC");
}

// Frontend URL for building email links (password reset, etc.)
builder.Services.Configure<FrontendSettings>(builder.Configuration.GetSection("FrontendSettings"));

// Add JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Ensure JWT claim types ("role", "sub") are remapped to ClaimTypes URIs.
    // Without this, User.IsInRole() and User.FindFirst(ClaimTypes.Role) fail on .NET 10.
    options.MapInboundClaims = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

// Add Authorization Policies
// ARCHITECTURAL PRINCIPLE: APIs under [Authorize(Policy=xx)] should NOT require 
// parameters that can be derived from JWT token claims
// 
// SECURITY MODEL (Angular/API Architecture):
// - JWT tokens are cryptographically signed and cannot be forged by users
// - Backend services scope all queries using token claims (regId, jobPath, userId)
// - Route-based jobPath validation is unnecessary - token signature provides security
// - MVC-style URL manipulation attacks do not apply to signed JWT API architecture

builder.Services.AddAuthorization(options =>
{
    // Default policy: require authenticated user only
    // Token signature verification handles security; no route validation needed
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("SuperUserOnly", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, RoleConstants.Names.SuperuserName));


    options.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role,
            RoleConstants.Names.SuperuserName,
            RoleConstants.Names.DirectorName,
            RoleConstants.Names.SuperDirectorName));

    // Roles that may enter game scores: admin roles + event-day Scorer.
    // Capability-named (like CanCrossCustomerJobs); same name as the legacy CanScore policy.
    options.AddPolicy("CanScore", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role,
            RoleConstants.Names.SuperuserName,
            RoleConstants.Names.DirectorName,
            RoleConstants.Names.SuperDirectorName,
            RoleConstants.Names.ScorerName));

    options.AddPolicy("RefAdmin", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role,
            RoleConstants.Names.SuperuserName,
            RoleConstants.Names.DirectorName,
            RoleConstants.Names.RefAssignorName));

    options.AddPolicy("StoreAdmin", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role,
            RoleConstants.Names.SuperuserName,
            RoleConstants.Names.DirectorName,
            RoleConstants.Names.StoreAdminName));

    options.AddPolicy("CanCrossCustomerJobs", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role,
            RoleConstants.Names.SuperuserName,
            RoleConstants.Names.SuperDirectorName));

    options.AddPolicy("TeamMembersOnly", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role,
            RoleConstants.Names.StaffName,
            RoleConstants.Names.FamilyName,
            RoleConstants.Names.PlayerName));

    options.AddPolicy("TeamMembersAndHigher", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role,
            RoleConstants.Names.StaffName,
            RoleConstants.Names.FamilyName,
            RoleConstants.Names.PlayerName,
            RoleConstants.Names.DirectorName,
            RoleConstants.Names.SuperDirectorName,
            RoleConstants.Names.SuperuserName));

    options.AddPolicy("StaffOnly", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role,
            RoleConstants.Names.UnassignedAdultName,
            RoleConstants.Names.StaffName));
});

builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer((schema, context, cancellationToken) =>
    {
        // Ensure numeric types are exported as number (not number | string)
        // and nullable variants include Null in the type union
        var jsonType = context.JsonTypeInfo;
        if (jsonType.Type == typeof(int))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer;
            schema.Format = "int32";
        }
        else if (jsonType.Type == typeof(int?))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer | Microsoft.OpenApi.JsonSchemaType.Null;
            schema.Format = "int32";
        }
        else if (jsonType.Type == typeof(long))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer;
            schema.Format = "int64";
        }
        else if (jsonType.Type == typeof(long?))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer | Microsoft.OpenApi.JsonSchemaType.Null;
            schema.Format = "int64";
        }
        else if (jsonType.Type == typeof(short))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer;
            schema.Format = "int32";
        }
        else if (jsonType.Type == typeof(short?))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer | Microsoft.OpenApi.JsonSchemaType.Null;
            schema.Format = "int32";
        }
        else if (jsonType.Type == typeof(byte))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer;
            schema.Format = "int32";
        }
        else if (jsonType.Type == typeof(byte?))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer | Microsoft.OpenApi.JsonSchemaType.Null;
            schema.Format = "int32";
        }
        else if (jsonType.Type == typeof(decimal))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Number;
            schema.Format = "double";
        }
        else if (jsonType.Type == typeof(decimal?))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Number | Microsoft.OpenApi.JsonSchemaType.Null;
            schema.Format = "double";
        }
        else if (jsonType.Type == typeof(double))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Number;
            schema.Format = "double";
        }
        else if (jsonType.Type == typeof(double?))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Number | Microsoft.OpenApi.JsonSchemaType.Null;
            schema.Format = "double";
        }
        else if (jsonType.Type == typeof(float))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Number;
            schema.Format = "float";
        }
        else if (jsonType.Type == typeof(float?))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Number | Microsoft.OpenApi.JsonSchemaType.Null;
            schema.Format = "float";
        }
        return Task.CompletedTask;
    });
});

// HSTS: 1 year, includeSubDomains. Wildcard *.teamsportsinfo.com cert on prod
// covers every subdomain including customer-branded ones. Enabled below for
// non-Development environments only (Staging + Production).
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// CORS. Origins are topology, so they live in appsettings.{Environment}.json — the Ionic
// dev-server origin belongs in Development/Staging and must never reach the Production binary.
// Each overlay carries the FULL list: .NET merges configuration arrays by index, so a partial
// overlay silently leaves the base file's trailing entries in place.
//
// Wildcard patterns need SetIsOriginAllowedToAllowWildcardSubdomains() to take effect, and the
// bare domain is listed separately because *.teamsportsinfo.com does not match it. Customer-
// branded subdomains (mylaxclub.teamsportsinfo.com) hit the catchall site post-cutover.
//
// The mobile app's device origins (capacitor://localhost on iOS, https://localhost on Android)
// appear in every environment: it runs with CapacitorHttp disabled, so its requests leave the
// WebView as ordinary CORS-checked XHR even on a real handset, not just under `ionic serve`.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (corsOrigins.Length == 0)
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins is missing or empty. Every appsettings.{Environment}.json must "
        + "declare the complete origin list.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .SetIsOriginAllowedToAllowWildcardSubdomains()
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .WithExposedHeaders(
                  "Content-Disposition",
                  // ADN reconciliation run-monthly counts (read by frontend after blob download)
                  "X-Imported-Count",
                  "X-Skipped-Duplicates",
                  "X-Batches-Pulled",
                  "X-Transactions-Pulled",
                  "X-Iif-Reg-Trns-Source",
                  "X-Iif-Reg-Trns-Consolidated",
                  "X-Iif-Merch-Trns-Source",
                  "X-Iif-Merch-Trns-Consolidated");
    });
});

// ── Serilog Structured Logging ────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341")
    .CreateLogger();

builder.Host.UseSerilog();

// ── [STARTUP-CONFIG] audit to Seq. One LogInformation per category, all tagged
// boot_audit=true for Seq filtering. Secrets log first-4-char fingerprint only;
// full values never appear. Catastrophic-risk categories (db, jwt, adn, ses)
// first so log scanners hit them before ancillary entries.
{
    static string Fp4(string? s)
        => string.IsNullOrEmpty(s) ? "(unset)"
         : s.Length >= 4 ? s.Substring(0, 4) + "..."
         : "(short)";

    static (string server, string db) ParseConnStr(string? cs)
    {
        if (string.IsNullOrWhiteSpace(cs)) return ("(none)", "(none)");
        string server = "(none)", db = "(none)";
        foreach (var raw in cs.Split(';'))
        {
            var p = raw.TrimStart();
            if (p.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
                server = p.Substring("Server=".Length).Trim();
            else if (p.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
                db = p.Substring("Database=".Length).Trim();
            else if (p.StartsWith("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
                db = p.Substring("Initial Catalog=".Length).Trim();
        }
        return (server, db);
    }

    var cfg = builder.Configuration;
    var hostEnv = builder.Environment;
    var machine = System.Environment.MachineName;
    var isLive = hostEnv.IsProduction();
    var bootLog = Log.ForContext("boot_audit", true);

    bootLog.Information(
        "[STARTUP-CONFIG] host: env={Env} machine={Machine} isLiveProduction={IsLive}",
        hostEnv.EnvironmentName, machine, isLive);

    var (dbServer, dbName) = ParseConnStr(cfg.GetConnectionString("DefaultConnection"));
    bootLog.Information(
        "[STARTUP-CONFIG] db: server={Server} database={Database}",
        dbServer, dbName);

    bootLog.Information(
        "[STARTUP-CONFIG] jwt: issuer={Issuer} audience={Audience} signingKey={KeyFp}",
        cfg["JwtSettings:Issuer"] ?? "(unset)",
        cfg["JwtSettings:Audience"] ?? "(unset)",
        Fp4(cfg["JwtSettings:SecretKey"]));

    // Where the Data Protection key ring lives. This is audited because the failure it replaces was
    // SILENT: an in-memory ring is rebuilt on every pool recycle, invalidating every password-reset
    // link already in a user's inbox, and the victim just sees "expired link" -- so nothing ever gets
    // reported. persistedKeys>0 on a later boot is the proof the ring survived a restart. If this ever
    // reads keyRing=(ephemeral), password reset is quietly broken again.
    var keyRingDir = hostEnv.IsDevelopment()
        ? null
        : Path.Combine(hostEnv.ContentRootPath, "keys");
    bootLog.Information(
        "[STARTUP-CONFIG] dataProtection: keyRing={KeyRing} persistedKeys={KeyCount} resetTokenLifespan={Lifespan}",
        keyRingDir ?? "(dev default: %LOCALAPPDATA%)",
        keyRingDir is not null && Directory.Exists(keyRingDir)
            ? Directory.GetFiles(keyRingDir, "key-*.xml").Length
            : 0,
        "1h");

    bootLog.Information(
        "[STARTUP-CONFIG] adn: defaultMode={Mode} sandboxLoginIdFp={SandboxFp} sandboxTransactionKeyFp={SandboxTxFp} prodCredsSource=customer.AdnLoginId(per-job)",
        hostEnv.IsProduction() ? "PRODUCTION" : "SANDBOX",
        Fp4(cfg["AuthorizeNet:SandboxLoginId"] ?? Environment.GetEnvironmentVariable("ADN_SANDBOX_LOGINID")),
        Fp4(cfg["AuthorizeNet:SandboxTransactionKey"] ?? Environment.GetEnvironmentVariable("ADN_SANDBOX_TRANSACTIONKEY")));

    var awsRegion = cfg["EmailSettings:AwsRegion"]
                    ?? cfg["AWS:Region"]
                    ?? Environment.GetEnvironmentVariable("AWS_REGION")
                    ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
                    ?? "(default chain)";
    var awsAccessKey = cfg["AWS:AccessKey"] ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
    var awsSecretKey = cfg["AWS:SecretKey"] ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
    bootLog.Information(
        "[STARTUP-CONFIG] ses: region={Region} fromDefault={From} accessKeyFp={KeyFp} secretKeyFp={SecretFp} sendGate={Gate}",
        awsRegion, TsicConstants.SupportEmail, Fp4(awsAccessKey), Fp4(awsSecretKey), isLive ? "LIVE" : "sandbox");

    var viBase = cfg["VerticalInsure:BaseUrl"]
                  ?? Environment.GetEnvironmentVariable("VI_BASE_URL")
                  ?? "https://api.verticalinsure.com";
    var viConfigClientId = isLive
        ? (cfg["VerticalInsure:ProdClientId"] ?? Environment.GetEnvironmentVariable("VI_PROD_CLIENT_ID"))
        : (cfg["VerticalInsure:DevClientId"] ?? Environment.GetEnvironmentVariable("VI_DEV_CLIENT_ID"));
    var viConfigSecret = isLive
        ? (cfg["VerticalInsure:ProdSecret"] ?? Environment.GetEnvironmentVariable("VI_PROD_SECRET"))
        : (cfg["VerticalInsure:DevSecret"] ?? Environment.GetEnvironmentVariable("VI_DEV_SECRET"));
    bootLog.Information(
        "[STARTUP-CONFIG] verticalInsure: baseUrl={BaseUrl} hardcodedClientIdGate={Gate} configClientIdFp={Fp} configSecretFp={SecretFp}",
        viBase, isLive ? "live_..." : "test_...", Fp4(viConfigClientId), Fp4(viConfigSecret));

    var usLaxBase = cfg["UsLax:ApiBase"]
                     ?? Environment.GetEnvironmentVariable("USLAX_API_BASE")
                     ?? "https://api.usalacrosse.com/";
    var usLaxSecret = cfg["UsLax:Secret"] ?? Environment.GetEnvironmentVariable("USLAX_SECRET");
    var usLaxUsername = cfg["UsLax:Username"] ?? Environment.GetEnvironmentVariable("USLAX_USERNAME");
    var usLaxPassword = cfg["UsLax:Password"] ?? Environment.GetEnvironmentVariable("USLAX_PASSWORD");
    bootLog.Information(
        "[STARTUP-CONFIG] usLax: baseUrl={BaseUrl} clientIdFp={Fp} secretFp={SecretFp} usernameFp={UsernameFp} pwGate={PwGate}",
        usLaxBase,
        Fp4(cfg["UsLax:ClientId"] ?? Environment.GetEnvironmentVariable("USLAX_CLIENT_ID")),
        Fp4(usLaxSecret),
        Fp4(usLaxUsername),
        string.IsNullOrWhiteSpace(usLaxPassword) ? "unset" : "set");

    bootLog.Information(
        "[STARTUP-CONFIG] frontend: baseUrl={Url}",
        cfg["FrontendSettings:BaseUrl"] ?? "(unset)");

    bootLog.Information(
        "[STARTUP-CONFIG] seq: serverUrl={Url}",
        cfg["Seq:ServerUrl"] ?? "http://localhost:5341");

    bootLog.Information(
        "[STARTUP-CONFIG] cors: origins=[https://localhost:4200, https://*.teamsportsinfo.com, https://teamsportsinfo.com]");

    bootLog.Information(
        "[STARTUP-CONFIG] adnSweep: enabled={Enabled} hourLocal={Hour}",
        cfg["AdnSweep:Enabled"] ?? "(unset)",
        cfg["AdnSweep:SweepHourLocal"] ?? "(unset)");

    bootLog.Information(
        "[STARTUP-CONFIG] anthropic: model={Model} apiKeyFp={Fp}",
        cfg["Anthropic:Model"] ?? "(unset)",
        Fp4(cfg["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")));

    var firebaseRelPath = cfg["Firebase:CredentialFilePath"];
    var firebaseAbsPath = string.IsNullOrWhiteSpace(firebaseRelPath)
        ? null
        : Path.Combine(AppContext.BaseDirectory, firebaseRelPath);
    bootLog.Information(
        "[STARTUP-CONFIG] firebase: credentialFilePath={Path} fileExists={Exists}",
        firebaseRelPath ?? "(unset)",
        firebaseAbsPath != null && File.Exists(firebaseAbsPath));

    bootLog.Information(
        "[STARTUP-CONFIG] syncfusion: licenseKey={Fp}",
        Fp4(cfg["Syncfusion:LicenseKey"]));
}

var app = builder.Build();

// Add detailed error handling in development
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/swagger/v1/swagger.json");
}

// HTTPS enforcement: HSTS for non-Development (Staging + Production), then redirect
// any incoming HTTP to HTTPS when HTTPS is configured. HSTS belongs before redirect
// so the response carries the header.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Urls.Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
{
    app.UseHttpsRedirection();
}

// Response compression (before routing so all responses are compressed)
app.UseResponseCompression();

// Serve uploaded images from the BannerFiles directory.
// In dev this enables end-to-end image upload testing; in prod the CDN is primary
// but this acts as a fallback.
var bannerFilesPath = app.Configuration.GetSection("FileStorage")["BannerFilesPath"];
if (!string.IsNullOrEmpty(bannerFilesPath))
{
    try
    {
        if (!Directory.Exists(bannerFilesPath))
            Directory.CreateDirectory(bannerFilesPath);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(bannerFilesPath),
            RequestPath = "/static/BannerFiles"
        });
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "BannerFiles path '{Path}' is not accessible — static file serving disabled", bannerFilesPath);
    }
}

// Ensure proper middleware order for CORS
app.UseRouting();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("StatusCode", httpContext.Response.StatusCode);
    };
});
app.UseCors("AllowAngularApp");
app.UseAuthentication();
app.UseAuthorization();
// NOTE: no OPTIONS catch-all endpoint here. UseCors terminates preflights itself
// (verified: preflight → 204 from the middleware). A previous "{*path}" OPTIONS
// catch-all made every unknown route return 405 (path matched, method didn't)
// instead of 404, disguising missing-route errors as CORS/method problems.

app.MapControllers();
app.MapHub<TSIC.API.Hubs.ChatHub>("/hubs/chat");

await app.RunAsync();


