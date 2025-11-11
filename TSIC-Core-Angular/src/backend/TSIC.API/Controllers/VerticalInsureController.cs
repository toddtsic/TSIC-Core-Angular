using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Services;
using System.Diagnostics.CodeAnalysis;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/verticalInsure")]
public class VerticalInsureController : ControllerBase
{
    private readonly SqlDbContext _db;
    private readonly IJobLookupService _jobLookupService;
    private readonly ILogger<VerticalInsureController> _logger;

    public VerticalInsureController(SqlDbContext db, IJobLookupService jobLookupService, ILogger<VerticalInsureController> logger, IConfiguration config)
    {
        _db = db;
        _jobLookupService = jobLookupService;
        _logger = logger;
    }

    public record VIPlayerObjectResponse(
        string client_id,
        PaymentsDto payments,
        ThemeDto theme,
        ProductConfigDto product_config
    );

    public record PaymentsDto(bool enabled, bool button);
    public record ThemeDto(ColorsDto colors, string? font_family);
    public record ColorsDto(string? primary);

    public record ProductConfigDto(List<VIPlayerProductDto> registration_cancellation);

    public record VIPlayerProductDto(
        VICustomerDto customer,
        VIPlayerMetadataDto metadata,
        VIPlayerPolicyAttributes policy_attributes
    );

    public record VICustomerDto(
        string email_address,
        string first_name,
        string last_name,
        string? city,
        string? state,
        string? postal_code,
        string? phone
    );

    public record VIPlayerMetadataDto(
        string tsic_secondchance,
        string context_name,
        string context_event,
        string? context_description,
        Guid tsic_registrationid
    );

    public record VIPlayerPolicyAttributes(
        DateOnly event_start_date,
        DateOnly event_end_date,
        int insurable_amount,
        VIParticipantDto participant,
        VIOrganizationDto organization
    );

    public record VIParticipantDto(string first_name, string last_name);
    public record VIOrganizationDto(
        string? org_contact_email,
        string? org_contact_first_name,
        string? org_contact_last_name,
        string? org_contact_phone,
        string? org_name,
        bool payment_plan
    );

    /// <summary>
    /// Builds the VerticalInsure player registration object for the current family + job.
    /// </summary>
    /// <param name="jobPath">Job path segment</param>
    /// <param name="familyUserId">Family user id</param>
    /// <param name="secondChance">If true, enables embedded payment buttons</param>
    [AllowAnonymous]
    [HttpGet("player-object")]
    [SuppressMessage("Maintainability", "S3776:Refactor this method to reduce its Cognitive Complexity.", Justification = "Legacy parity and straightforward data shaping")]
    public async Task<ActionResult<VIPlayerObjectResponse>> GetPlayerObject([FromQuery] string jobPath, [FromQuery] string familyUserId, [FromQuery] bool secondChance = false)
    {
        _logger.LogInformation("[VI] Building player object for {JobPath} family {FamilyUserId}", jobPath, familyUserId);
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
        {
            return NotFound(new { message = $"Job not found: {jobPath}" });
        }

        var job = await _db.Jobs.AsNoTracking().Where(j => j.JobId == jobId).Select(j => new { j.JobName, j.BOfferPlayerRegsaverInsurance }).SingleOrDefaultAsync();
        if (job == null)
        {
            return NotFound(new { message = "Job not found." });
        }

        if (!(job.BOfferPlayerRegsaverInsurance ?? false))
        {
            // Feature disabled at job level
            return Ok(new VIPlayerObjectResponse(
                client_id: string.Empty,
                payments: new PaymentsDto(enabled: false, button: false),
                theme: new ThemeDto(new ColorsDto("purple"), "Fira Sans"),
                product_config: new ProductConfigDto(new List<VIPlayerProductDto>())
            ));
        }

        // Derive organization keys from first Director registration (legacy parity)
        var director = await _db.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.Role != null && r.Role.Name == "Director" && r.BActive == true)
            .OrderBy(r => r.RegistrationTs)
            .Select(r => new
            {
                r.RegistrationId,
                Email = r.User != null ? r.User.Email : null,
                FirstName = r.User != null ? r.User.FirstName : null,
                LastName = r.User != null ? r.User.LastName : null,
                Cellphone = r.User != null ? r.User.Cellphone : null,
                OrgName = r.Job != null ? r.Job.JobName : null,
                PaymentPlan = r.Job != null && (r.Job.AdnArb == true)
            })
            .FirstOrDefaultAsync();

        var org = new VIOrganizationDto(
            org_contact_email: director?.Email,
            org_contact_first_name: director?.FirstName,
            org_contact_last_name: director?.LastName,
            org_contact_phone: director?.Cellphone,
            org_name: director?.OrgName,
            payment_plan: director?.PaymentPlan ?? false
        );

        // Family contact info
        var family = await _db.Families.AsNoTracking()
            .Where(f => f.FamilyUserId == familyUserId)
            .Select(f => new
            {
                FirstName = f.MomFirstName,
                LastName = f.MomLastName,
                Email = f.MomEmail,
                Phone = f.MomCellphone,
                Street = f.FamilyUser.StreetAddress,
                City = f.FamilyUser.City,
                State = f.FamilyUser.State,
                Zip = f.FamilyUser.PostalCode
            })
            .SingleOrDefaultAsync();

        // Build product list per eligible registration (no existing policy, fee > 0, team not expired)
        var nowPlus1Day = DateTime.Now.AddHours(24);
        var registrations = await _db.Registrations.AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.FeeTotal > 0 && r.RegsaverPolicyId == null && r.AssignedTeam != null && r.AssignedTeam.Expireondate > nowPlus1Day)
            .Select(r => new
            {
                r.RegistrationId,
                r.Assignment,
                FirstName = r.User != null ? r.User.FirstName : null,
                LastName = r.User != null ? r.User.LastName : null,
                PerRegistrantFee = r.AssignedTeam != null ? r.AssignedTeam.PerRegistrantFee : null,
                TeamFee = (r.AssignedTeam != null && r.AssignedTeam.Agegroup != null) ? r.AssignedTeam.Agegroup.TeamFee : null,
                r.FeeTotal
            })
            .ToListAsync();

        var products = new List<VIPlayerProductDto>();
        foreach (var r in registrations)
        {
            var contextName = (job.JobName ?? string.Empty).Split(':')[0];
            int insurable = 0;
            // Prefer per-registrant fee when present; else fall back to team fee; else total fee
            if ((int?)(r.PerRegistrantFee ?? 0) > 0)
            {
                insurable = (int)((r.PerRegistrantFee ?? 0) * 100);
            }
            else if ((int?)(r.TeamFee ?? 0) > 0)
            {
                insurable = (int)((r.TeamFee ?? 0) * 100);
            }
            else
            {
                insurable = (int)(r.FeeTotal * 100);
            }

            var customer = new VICustomerDto(
                email_address: family?.Email ?? string.Empty,
                first_name: family?.FirstName ?? string.Empty,
                last_name: family?.LastName ?? string.Empty,
                city: family?.City,
                state: family?.State,
                postal_code: family?.Zip,
                phone: family?.Phone
            );
            var metadata = new VIPlayerMetadataDto(
                tsic_secondchance: secondChance ? "1" : "0",
                context_name: contextName,
                context_event: job.JobName ?? contextName,
                context_description: r.Assignment,
                tsic_registrationid: r.RegistrationId
            );
            var policy = new VIPlayerPolicyAttributes(
                event_start_date: DateOnly.FromDateTime(DateTime.Now).AddDays(1),
                event_end_date: DateOnly.FromDateTime(DateTime.Now).AddYears(1),
                insurable_amount: insurable,
                participant: new VIParticipantDto(r.FirstName ?? string.Empty, r.LastName ?? string.Empty),
                organization: org
            );
            products.Add(new VIPlayerProductDto(customer, metadata, policy));
        }

        // Hard-coded credentials per request (live keys); in production move to secure secrets provider.
        var clientId = "live_VJ8O8O81AZQ8MCSKWM98928597WUHSMS";
        // Note: Secret intentionally not used here.
        var response = new VIPlayerObjectResponse(
            client_id: clientId,
            payments: new PaymentsDto(enabled: secondChance, button: false),
            theme: new ThemeDto(new ColorsDto("purple"), "Fira Sans"),
            product_config: new ProductConfigDto(products)
        );
        return Ok(response);
    }
}
