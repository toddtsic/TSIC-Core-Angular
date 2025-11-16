using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using TSIC.API.Dtos;
using TSIC.API.Dtos.VerticalInsure;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

/// <summary>
/// Encapsulates all VerticalInsure / RegSaver snapshot generation logic.
/// Responsibilities: eligibility filtering, product construction, environment-based client id selection.
/// </summary>
public sealed class VerticalInsureService : IVerticalInsureService
{
    private readonly SqlDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly ILogger<VerticalInsureService> _logger;
    private readonly ITeamLookupService _teamLookupService;

    public VerticalInsureService(SqlDbContext db, IHostEnvironment env, ILogger<VerticalInsureService> logger, ITeamLookupService teamLookupService)
    {
        _db = db;
        _env = env;
        _logger = logger;
        _teamLookupService = teamLookupService;
    }

    public async Task<PreSubmitInsuranceDto> BuildOfferAsync(Guid jobId, string familyUserId)
    {
        try
        {
            var jobOffer = await _db.Jobs
                .Where(j => j.JobId == jobId)
                .Select(j => new { j.JobName, j.BOfferPlayerRegsaverInsurance })
                .SingleOrDefaultAsync();
            if (jobOffer == null || !(jobOffer.BOfferPlayerRegsaverInsurance ?? false))
            {
                return new PreSubmitInsuranceDto { Available = false };
            }

            var regs = await GetEligibleRegistrationsAsync(jobId, familyUserId);
            if (regs.Count == 0)
            {
                return new PreSubmitInsuranceDto { Available = false };
            }

            var family = await GetFamilyContactAsync(familyUserId);
            var director = await GetDirectorContactAsync(jobId);
            var products = await BuildProductsAsync(regs, family, director, jobOffer.JobName);
            var playerObj = BuildPlayerObject(products);
            return new PreSubmitInsuranceDto
            {
                Available = true,
                PlayerObject = playerObj,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                StateId = $"vi-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VerticalInsure] Failed to build snapshot.");
            return new PreSubmitInsuranceDto { Available = false, Error = "Snapshot generation failed." };
        }
    }

    private async Task<List<(Guid RegistrationId, Guid AssignedTeamId, string? Assignment, string? FirstName, string? LastName, decimal? PerRegistrantFee, decimal? TeamFee, decimal FeeTotal)>> GetEligibleRegistrationsAsync(Guid jobId, string familyUserId)
    {
        var cutoff = DateTime.Now.AddHours(24);
        var regs = await _db.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.FeeTotal > 0 && r.RegsaverPolicyId == null && r.AssignedTeam != null && r.AssignedTeam.Expireondate > cutoff)
            .Select(r => new
            {
                r.RegistrationId,
                AssignedTeamId = r.AssignedTeamId!.Value,
                r.Assignment,
                FirstName = r.User != null ? r.User.FirstName : null,
                LastName = r.User != null ? r.User.LastName : null,
                PerRegistrantFee = r.AssignedTeam != null ? r.AssignedTeam.PerRegistrantFee : null,
                TeamFee = (r.AssignedTeam != null && r.AssignedTeam.Agegroup != null) ? r.AssignedTeam.Agegroup.TeamFee : null,
                r.FeeTotal
            })
            .ToListAsync();
        return regs.Select(r => (r.RegistrationId, r.AssignedTeamId, r.Assignment, r.FirstName, r.LastName, r.PerRegistrantFee, r.TeamFee, r.FeeTotal)).ToList();
    }

    private async Task<(string? FirstName, string? LastName, string? Email, string? Phone, string? City, string? State, string? Zip)?> GetFamilyContactAsync(string familyUserId)
    {
        var f = await _db.Families.AsNoTracking()
            .Where(x => x.FamilyUserId == familyUserId)
            .Select(x => new
            {
                FirstName = x.MomFirstName,
                LastName = x.MomLastName,
                Email = x.MomEmail,
                Phone = x.MomCellphone,
                City = x.FamilyUser.City,
                State = x.FamilyUser.State,
                Zip = x.FamilyUser.PostalCode
            })
            .SingleOrDefaultAsync();
        if (f == null) return null;
        return (f.FirstName, f.LastName, f.Email, f.Phone, f.City, f.State, f.Zip);
    }

    private async Task<(string? Email, string? FirstName, string? LastName, string? Cellphone, string? OrgName, bool PaymentPlan)?> GetDirectorContactAsync(Guid jobId)
    {
        var d = await _db.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.Role != null && r.Role.Name == "Director" && r.BActive == true)
            .OrderBy(r => r.RegistrationTs)
            .Select(r => new
            {
                Email = r.User != null ? r.User.Email : null,
                FirstName = r.User != null ? r.User.FirstName : null,
                LastName = r.User != null ? r.User.LastName : null,
                Cellphone = r.User != null ? r.User.Cellphone : null,
                OrgName = r.Job != null ? r.Job.JobName : null,
                PaymentPlan = r.Job != null && (r.Job.AdnArb == true)
            })
            .FirstOrDefaultAsync();
        if (d == null) return null;
        return (d.Email, d.FirstName, d.LastName, d.Cellphone, d.OrgName, d.PaymentPlan);
    }

    private async Task<List<VIPlayerProductDto>> BuildProductsAsync(
        List<(Guid RegistrationId, Guid AssignedTeamId, string? Assignment, string? FirstName, string? LastName, decimal? PerRegistrantFee, decimal? TeamFee, decimal FeeTotal)> regs,
        (string? FirstName, string? LastName, string? Email, string? Phone, string? City, string? State, string? Zip)? family,
        (string? Email, string? FirstName, string? LastName, string? Cellphone, string? OrgName, bool PaymentPlan)? director,
        string? jobName)
    {
        var products = new List<VIPlayerProductDto>();
        var contextName = (jobName ?? string.Empty).Split(':')[0];
        foreach (var r in regs)
        {
            // Centralized fee resolution for insurable amount: prefer per-registrant fee from resolver; fallback to fee total.
            var (fee, _) = await _teamLookupService.ResolvePerRegistrantAsync(r.AssignedTeamId);
            var insurable = ComputeInsurableAmountFromCentralized(fee, r.PerRegistrantFee, r.TeamFee, r.FeeTotal);
            var product = new VIPlayerProductDto
            {
                customer = new VICustomerDto
                {
                    email_address = family?.Email ?? string.Empty,
                    first_name = family?.FirstName ?? string.Empty,
                    last_name = family?.LastName ?? string.Empty,
                    city = family?.City ?? string.Empty,
                    state = family?.State ?? string.Empty,
                    postal_code = family?.Zip ?? string.Empty,
                    phone = family?.Phone ?? string.Empty,
                    street = string.Empty
                },
                metadata = new VIPlayerMetadataDto
                {
                    tsic_secondchance = "0",
                    context_name = contextName,
                    context_event = jobName ?? contextName,
                    context_description = r.Assignment ?? string.Empty,
                    tsic_registrationid = r.RegistrationId
                },
                policy_attributes = new VIPlayerPolicyAttributes
                {
                    event_start_date = DateOnly.FromDateTime(DateTime.Now).AddDays(1),
                    event_end_date = DateOnly.FromDateTime(DateTime.Now).AddYears(1),
                    insurable_amount = insurable,
                    participant = new VIParticipantDto { first_name = r.FirstName ?? string.Empty, last_name = r.LastName ?? string.Empty },
                    organization = new VIOrganizationDto
                    {
                        org_contact_email = director?.Email ?? string.Empty,
                        org_contact_first_name = director?.FirstName ?? string.Empty,
                        org_contact_last_name = director?.LastName ?? string.Empty,
                        org_contact_phone = director?.Cellphone ?? string.Empty,
                        org_name = director?.OrgName ?? string.Empty,
                        payment_plan = director?.PaymentPlan ?? false
                    }
                }
            };
            products.Add(product);
        }
        return products;
    }

    private VIPlayerObjectResponse BuildPlayerObject(List<VIPlayerProductDto> products)
    {
        var clientId = _env.IsDevelopment() ?
            "test_GREVHKFHJY87CGWW9RF15JD50W5PPQ7U" :
            "live_VJ8O8O81AZQ8MCSKWM98928597WUHSMS";
        return new VIPlayerObjectResponse
        {
            client_id = clientId,
            payments = new VIPaymentsDto { enabled = false, button = false },
            theme = new VIThemeDto
            {
                colors = new VIColorsDto { primary = "purple" },
                font_family = "Fira Sans",
                components = new VIComponentsDto()
            },
            product_config = new VIPlayerProductConfigDto
            {
                registration_cancellation = products
            }
        };
    }

    private static int ComputeInsurableAmount(decimal amount)
        => (int)(amount * 100);

    private static int ComputeInsurableAmountFromCentralized(decimal centralizedFee, decimal? perRegistrantFee, decimal? teamFee, decimal feeTotal)
    {
        if (centralizedFee > 0m) return ComputeInsurableAmount(centralizedFee);
        if (perRegistrantFee.HasValue && perRegistrantFee.Value > 0) return ComputeInsurableAmount(perRegistrantFee.Value);
        if (teamFee.HasValue && teamFee.Value > 0) return ComputeInsurableAmount(teamFee.Value);
        return ComputeInsurableAmount(feeTotal);
    }
}
