using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Data.Identity;
using TSIC.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using TSIC.Application.Services;
using TSIC.Application.Validators;
using TSIC.Infrastructure.Services;
using TSIC.API.Services;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Validation;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Email;
using Amazon.SimpleEmail;
using Amazon;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddMemoryCache(); // Add memory cache for refresh tokens
builder.Services.AddScoped<IRoleLookupService, RoleLookupService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IJobLookupService, JobLookupService>();
builder.Services.AddScoped<ITeamLookupService, TeamLookupService>();
builder.Services.AddScoped<IAdnApiService, AdnApiService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IPlayerBaseTeamFeeResolverService, PlayerBaseTeamFeeResolverService>();
builder.Services.AddScoped<IRegistrationRecordFeeCalculatorService, RegistrationRecordFeeCalculatorService>();
builder.Services.AddScoped<IPlayerRegistrationService, PlayerRegistrationService>();
builder.Services.AddScoped<IPlayerFormValidationService, PlayerFormValidationService>();
builder.Services.AddScoped<IPlayerRegistrationFeeService, PlayerRegistrationFeeService>();
builder.Services.AddScoped<IPlayerRegistrationMetadataService, PlayerRegistrationMetadataService>();
builder.Services.AddScoped<IVerticalInsureService, VerticalInsureService>();
builder.Services.AddScoped<IDiscountCodeEvaluator, DiscountCodeEvaluatorService>();
builder.Services.AddScoped<ITextSubstitutionService, TextSubstitutionService>();
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
builder.Services.AddScoped<ITeamRegistrationService, TeamRegistrationService>();
builder.Services.AddScoped<IProfileMetadataService, ProfileMetadataService>();
builder.Services.AddScoped<IRegistrationQueryService, RegistrationQueryService>();
builder.Services.AddScoped<IUsLaxService, UsLaxService>();
builder.Services.AddScoped<ITokenService, TokenService>();
// Email (Amazon SES only)
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddSingleton<IAmazonSimpleEmailService>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<EmailSettings>>().Value;
    // If region specified use it, else default chain.
    if (!string.IsNullOrWhiteSpace(opts.AwsRegion))
    {
        var region = Amazon.RegionEndpoint.GetBySystemName(opts.AwsRegion);
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

// Profile Migration Services
builder.Services.AddHttpClient<GitHubProfileFetcher>();
builder.Services.AddScoped<CSharpToMetadataParser>();
builder.Services.AddScoped<ProfileMetadataMigrationService>();

// Add FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

builder.Services.AddDbContext<SqlDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        x => x.UseNetTopologySuite()));

// Separate DbContext for Identity operations only
builder.Services.AddDbContext<TsicIdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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
    .AddEntityFrameworkStores<TsicIdentityDbContext>();

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
builder.Services.AddAuthorization(options =>
{
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "TSIC API", Version = "v1" });
    options.SupportNonNullableReferenceTypes();
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
                  "https://cp-ng.teamsportsinfo.com")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Add detailed error handling in development
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TSIC API V1");
    });
}

// Conditionally use HTTPS redirection only when HTTPS is configured
if (app.Urls.Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
{
    app.UseHttpsRedirection();
}

// Ensure proper middleware order for CORS
app.UseRouting();
app.UseCors("AllowAngularApp");
app.UseAuthentication();
app.UseAuthorization();
// Explicitly handle preflight requests for any route and ensure CORS headers are applied
app.MapMethods("{*path}", new[] { "OPTIONS" }, () => Results.Ok())
    .RequireCors("AllowAngularApp");

app.MapControllers();

await app.RunAsync();
