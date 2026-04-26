using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Data.Identity;
using TSIC.Infrastructure.Repositories;
using TSIC.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
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
using TSIC.API.Services.Echeck;
using TSIC.API.Services.Email;
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
builder.Services.AddScoped<TSIC.Contracts.Services.IPlayerFeeCalculator, PlayerFeeCalculator>();
builder.Services.AddScoped<IPlayerRegistrationService, PlayerRegistrationService>();
builder.Services.AddScoped<IPlayerFormValidationService, PlayerFormValidationService>();
builder.Services.AddScoped<IFeeResolutionService, FeeResolutionService>();
builder.Services.AddScoped<IPlayerRegistrationMetadataService, PlayerRegistrationMetadataService>();
builder.Services.AddScoped<IRegistrationFeeAdjustmentService, RegistrationFeeAdjustmentService>();
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
builder.Services.AddScoped<IClubService, ClubService>();
builder.Services.AddScoped<IClubRosterService, ClubRosterService>();
builder.Services.AddScoped<IMyRosterService, TSIC.API.Services.MyRoster.MyRosterService>();
builder.Services.AddScoped<IUserPrivilegeLevelService, UserPrivilegeLevelService>();
builder.Services.AddScoped<ITeamPlacementService, TeamPlacementService>();
builder.Services.AddScoped<ITeamRegistrationService, TeamRegistrationService>();
builder.Services.AddScoped<IProfileMetadataService, ProfileMetadataService>();
builder.Services.AddScoped<IRegistrationQueryService, RegistrationQueryService>();
builder.Services.AddScoped<IUsLaxService, UsLaxService>();
builder.Services.AddScoped<IUsLaxMembershipService, UsLaxMembershipService>();
builder.Services.AddScoped<ITokenService, TokenService>();
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
// Customer Job Revenue (SuperUser financial dashboard)
builder.Services.AddScoped<ICustomerJobRevenueService, CustomerJobRevenueService>();
// Reporting
builder.Services.Configure<ReportingSettings>(builder.Configuration.GetSection("Reporting"));
builder.Services.AddScoped<IReportingService, ReportingService>();
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
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddScoped<IEmailHealthService, EmailHealthService>();

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

// eCheck settlement sweep (BackgroundService config; defaults apply if section absent)
builder.Services.Configure<EcheckSweepOptions>(builder.Configuration.GetSection("EcheckSweep"));
builder.Services.AddScoped<IEcheckSettlementRepository, EcheckSettlementRepository>();
builder.Services.AddScoped<IEcheckSweepService, EcheckSweepService>();
builder.Services.AddHostedService<EcheckSweepBackgroundService>();

// Profile Migration Services
builder.Services.AddScoped<IGitHubProfileFetcher, GitHubProfileFetcher>();
builder.Services.AddScoped<CSharpToMetadataParser>();
builder.Services.AddScoped<ProfileMetadataMigrationService>();

// Add FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

// Only register SqlServer DbContexts if not running tests
// Tests will provide in-memory versions via WebApplicationTestFactory
if (!builder.Environment.IsEnvironment("Testing"))
{
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

// CORS for Angular
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:4200",
                  "https://localhost:4200",
                  "http://cp-ng.teamsportsinfo.com",
                  "https://cp-ng.teamsportsinfo.com",
                  "http://dev.teamsportsinfo.com",
                  "https://dev.teamsportsinfo.com",
                  "http://claude-app.teamsportsinfo.com",
                  "https://claude-app.teamsportsinfo.com",
                  "http://bear.teamsportsinfo.com",
                  "https://bear.teamsportsinfo.com",
                  "http://www.teamsportsinfo.com",
                  "https://www.teamsportsinfo.com",
                  "http://teamsportsinfo.com",
                  "https://teamsportsinfo.com")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .WithExposedHeaders("Content-Disposition");
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

// Conditionally use HTTPS redirection only when HTTPS is configured
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
// Explicitly handle preflight requests for any route and ensure CORS headers are applied
app.MapMethods("{*path}", new[] { "OPTIONS" }, () => Results.Ok())
    .RequireCors("AllowAngularApp");

app.MapControllers();
app.MapHub<TSIC.API.Hubs.ChatHub>("/hubs/chat");

await app.RunAsync();


