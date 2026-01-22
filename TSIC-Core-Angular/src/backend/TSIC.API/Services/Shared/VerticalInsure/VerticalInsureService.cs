using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.VerticalInsure;
using TSIC.Domain.Entities;
using TSIC.Contracts.Repositories;
using TSIC.Application.Services.Shared.Insurance;
using TSIC.API.Services.Teams;

namespace TSIC.API.Services.Shared.VerticalInsure;

/// <summary>
/// Encapsulates all VerticalInsure / RegSaver snapshot generation logic.
/// Responsibilities: eligibility filtering, product construction, environment-based client id selection.
/// </summary>
public sealed partial class VerticalInsureService : IVerticalInsureService
{
    private readonly IJobRepository _jobRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IFamilyRepository _familyRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IUserRepository _userRepo;
    private readonly IHostEnvironment _env;
    private readonly ILogger<VerticalInsureService> _logger;
    private readonly ITeamLookupService _teamLookupService;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOptions<VerticalInsureSettings> _options;

    public VerticalInsureService(
        IJobRepository jobRepo,
        IRegistrationRepository registrationRepo,
        IFamilyRepository familyRepo,
        ITeamRepository teamRepo,
        IUserRepository userRepo,
        IHostEnvironment env,
        ILogger<VerticalInsureService> logger,
        ITeamLookupService teamLookupService,
        IOptions<VerticalInsureSettings> options,
        IHttpClientFactory? httpClientFactory = null)
    {
        _jobRepo = jobRepo;
        _registrationRepo = registrationRepo;
        _familyRepo = familyRepo;
        _teamRepo = teamRepo;
        _userRepo = userRepo;
        _env = env;
        _logger = logger;
        _teamLookupService = teamLookupService;
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PreSubmitInsuranceDto> BuildOfferAsync(Guid jobId, string familyUserId)
    {
        try
        {
            var jobOffer = await _jobRepo.GetInsuranceOfferInfoAsync(jobId);
            if (jobOffer == null || !jobOffer.BOfferPlayerRegsaverInsurance)
            {
                return new PreSubmitInsuranceDto { Available = false };
            }

            var regs = await _registrationRepo.GetEligibleInsuranceRegistrationsAsync(jobId, familyUserId);
            if (regs.Count == 0)
            {
                return new PreSubmitInsuranceDto { Available = false };
            }

            var family = await _familyRepo.GetFamilyContactAsync(familyUserId);
            var director = await _registrationRepo.GetDirectorContactForJobAsync(jobId);
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

    public async Task<VerticalInsurePurchaseResult> PurchasePoliciesAsync(Guid jobId, string familyUserId, IReadOnlyCollection<Guid> registrationIds, IReadOnlyCollection<string> quoteIds, string? token, CreditCardInfo? card, CancellationToken ct = default)
    {
        var result = new VerticalInsurePurchaseResult();
        try
        {
            // 1. Validate & load registrations
            var regs = await ValidateAndLoadAsync(jobId, familyUserId, registrationIds, quoteIds, result, ct);
            if (!result.Success) return result; // Early exit on validation failure

            // 2. Execute real purchase if HttpClientFactory available, else stub
            if (_httpClientFactory != null)
            {
                await ExecuteHttpPurchaseAsync(regs, familyUserId, quoteIds, token, card, result, ct);
            }
            else
            {
                ApplyStubPurchase(regs, familyUserId, result);
                await _registrationRepo.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VerticalInsure] purchase failed.");
            result.Success = false;
            result.Error = "Insurance purchase failed.";
        }
        return result;
    }

    private async Task<List<Registrations>> ValidateAndLoadAsync(Guid jobId, string familyUserId, IReadOnlyCollection<Guid> registrationIds, IReadOnlyCollection<string> quoteIds, VerticalInsurePurchaseResult result, CancellationToken ct)
    {
        if (registrationIds.Count == 0 && quoteIds.Count == 0)
        {
            result.Success = false; result.Error = "No registrations and no quotes supplied."; return new();
        }
        if (registrationIds.Count == 0)
        {
            result.Success = false; result.Error = "No registration IDs supplied."; return new();
        }
        if (quoteIds.Count == 0)
        {
            result.Success = false; result.Error = "No insurance quote IDs supplied."; return new();
        }
        if (registrationIds.Count != quoteIds.Count)
        {
            result.Success = false; result.Error = "Registration / quote count mismatch."; return new();
        }
        var regs = await _registrationRepo.ValidateRegistrationsForInsuranceAsync(jobId, familyUserId, registrationIds, ct);
        if (regs.Count == 0)
        {
            result.Success = false; result.Error = "No matching registrations found."; return new();
        }
        if (regs.Exists(r => !string.IsNullOrWhiteSpace(r.RegsaverPolicyId)))
        {
            result.Success = false; result.Error = "One or more registrations already have a policy."; return new();
        }
        result.Success = true; // mark validation success
        return regs;
    }

    private async Task ExecuteHttpPurchaseAsync(List<Registrations> regs, string familyUserId, IReadOnlyCollection<string> quoteIds, string? token, CreditCardInfo? card, VerticalInsurePurchaseResult result, CancellationToken ct)
    {
        var client = _httpClientFactory!.CreateClient("verticalinsure");
        var (clientId, clientSecret) = ResolveCredentials();
        var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        var payload = BuildBatchPayload(quoteIds, token, card);
        var req = new HttpRequestMessage(HttpMethod.Post, "v1/purchase/registration-cancellation/batch")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Authorization", $"Basic {authString}");
        req.Headers.Add("User-Agent", "TSIC.API HttpClient");

        var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode || !(response.StatusCode == System.Net.HttpStatusCode.Created || response.StatusCode == System.Net.HttpStatusCode.OK))
        {
            result.Success = false; result.Error = $"Insurance purchase HTTP error: {(int)response.StatusCode}"; return;
        }
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var policies = await JsonSerializer.DeserializeAsync<List<VIMakePlayerPaymentResponseDto>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct) ?? new();
        foreach (var policy in policies)
        {
            if (policy.policy_status == "ACTIVE" && !string.IsNullOrWhiteSpace(policy.policy_number))
            {
                var reg = regs.Find(r => r.RegistrationId == policy.metadata.TsicRegistrationId);
                if (reg != null)
                {
                    reg.RegsaverPolicyId = policy.policy_number;
                    reg.RegsaverPolicyIdCreateDate = DateTime.UtcNow;
                    reg.Modified = DateTime.UtcNow;
                    reg.LebUserId = familyUserId;
                    result.Policies[reg.RegistrationId] = policy.policy_number;
                }
            }
        }
        await _registrationRepo.SaveChangesAsync(ct);
        result.Success = true;
    }

    private static VIMakeTokenBatchCCPaymentDto BuildBatchPayload(IReadOnlyCollection<string> quoteIds, string? token, CreditCardInfo? card)
    {
        var dto = new VIMakeTokenBatchCCPaymentDto
        {
            quotes = quoteIds.Select(q => new VIMakeTokenBatchQuotesDto { quote_id = q }).ToList(),
            payment_method = !string.IsNullOrWhiteSpace(token)
                ? new VIMakeTokenBatchPaymentMethodDto
                {
                    token = $"stripe:{token}",
                    card = new VICreditCardDto
                    {
                        number = string.Empty,
                        verification = string.Empty,
                        month = string.Empty,
                        year = string.Empty,
                        name = string.Empty,
                        address_postal_code = string.Empty
                    }
                }
                : new VIMakeTokenBatchPaymentMethodDto
                {
                    token = string.Empty,
                    card = new VICreditCardDto
                    {
                        number = card?.Number ?? string.Empty,
                        verification = card?.Code ?? string.Empty,
                        month = card?.Expiry?.Length >= 2 ? card.Expiry.Substring(0, 2) : string.Empty,
                        year = card?.Expiry?.Length == 4 ? "20" + card.Expiry.Substring(2, 2) : string.Empty,
                        name = ($"{card?.FirstName} {card?.LastName}").Trim(),
                        address_postal_code = card?.Zip ?? string.Empty
                    }
                }
        };
        return dto;
    }

    private static void ApplyStubPurchase(IEnumerable<Registrations> regs, string familyUserId, VerticalInsurePurchaseResult result)
    {
        foreach (var reg in regs)
        {
            var policyNo = $"POL-{reg.RegistrationId.ToString("N").Substring(0, 8).ToUpper()}";
            reg.RegsaverPolicyId = policyNo;
            reg.RegsaverPolicyIdCreateDate = DateTime.UtcNow;
            reg.Modified = DateTime.UtcNow;
            reg.LebUserId = familyUserId;
            result.Policies[reg.RegistrationId] = policyNo;
        }
        result.Success = true;
    }

    private async Task<List<VIPlayerProductDto>> BuildProductsAsync(
        List<EligibleInsuranceRegistration> regs,
        FamilyContactInfo? family,
        DirectorContactInfo? director,
        string? jobName)
    {
        var products = new List<VIPlayerProductDto>();
        var contextName = (jobName ?? string.Empty).Split(':')[0];
        foreach (var r in regs)
        {
            // Centralized fee resolution for insurable amount: prefer per-registrant fee from resolver; fallback to fee total.
            var (fee, _) = await _teamLookupService.ResolvePerRegistrantAsync(r.AssignedTeamId);
            var insurable = InsurableAmountCalculator.ComputeInsurableAmountFromCentralized(fee, r.PerRegistrantFee, r.TeamFee, r.FeeTotal);
            var product = new VIPlayerProductDto
            {
                Customer = new VICustomerDto
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
                Metadata = new VIPlayerMetadataDto
                {
                    TsicSecondChance = "0",
                    ContextName = contextName,
                    ContextEvent = jobName ?? contextName,
                    ContextDescription = r.Assignment ?? string.Empty,
                    TsicRegistrationId = r.RegistrationId
                },
                PolicyAttributes = new VIPlayerPolicyAttributes
                {
                    EventStartDate = DateOnly.FromDateTime(DateTime.Now).AddDays(1),
                    EventEndDate = DateOnly.FromDateTime(DateTime.Now).AddYears(1),
                    InsurableAmount = insurable,
                    Participant = new VIParticipantDto { FirstName = r.FirstName ?? string.Empty, LastName = r.LastName ?? string.Empty },
                    Organization = new VIOrganizationDto
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
        // Hard-coded client id selection (dev vs prod)
        const string DEV_CLIENT_ID = "test_GREVHKFHJY87CGWW9RF15JD50W5PPQ7U";
        const string PROD_CLIENT_ID = "live_VJ8O8O81AZQ8MCSKWM98928597WUHSMS";
        var clientId = _env.IsDevelopment() ? DEV_CLIENT_ID : PROD_CLIENT_ID;
        return new VIPlayerObjectResponse
        {
            ClientId = clientId,
            Payments = new VIPaymentsDto { enabled = false, button = false },
            Theme = new VIThemeDto
            {
                colors = new VIColorsDto
                {
                    primary = "#0ea5e9",  // Sky blue
                    background = "var(--bs-body-bg)",  // Adapts to light/dark mode
                    border = "var(--bs-border-color)"  // Adapts to light/dark mode
                },
                font_family = "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
                components = new VIComponentsDto()
            },
            ProductConfig = new VIPlayerProductConfigDto
            {
                RegistrationCancellation = products
            }
        };
    }

    private (string clientId, string clientSecret) ResolveCredentials()
    {
        var s = _options.Value;
        if (_env.IsDevelopment())
        {
            var clientId = s.DevClientId ?? Environment.GetEnvironmentVariable("VI_DEV_CLIENT_ID") ?? string.Empty;
            var clientSecret = s.DevSecret ?? Environment.GetEnvironmentVariable("VI_DEV_SECRET") ?? string.Empty;
            return (clientId, clientSecret);
        }
        else
        {
            var clientId = s.ProdClientId ?? Environment.GetEnvironmentVariable("VI_PROD_CLIENT_ID") ?? string.Empty;
            var clientSecret = s.ProdSecret ?? Environment.GetEnvironmentVariable("VI_PROD_SECRET") ?? string.Empty;
            return (clientId, clientSecret);
        }
    }
}