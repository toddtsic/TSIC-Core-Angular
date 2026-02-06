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
using TeamEntity = TSIC.Domain.Entities.Teams;

namespace TSIC.API.Services.Shared.VerticalInsure;

/// <summary>
/// Team insurance extension methods for VerticalInsureService.
/// </summary>
public partial class VerticalInsureService
{
    public async Task<PreSubmitTeamInsuranceDto> BuildTeamOfferAsync(Guid regId, string userId)
    {
        try
        {
            // Get club rep registration to derive jobId
            var registrations = await _registrationRepo.GetByIdsAsync([regId]);
            var clubRepReg = registrations.FirstOrDefault();
            if (clubRepReg == null || clubRepReg.UserId != userId)
            {
                return new PreSubmitTeamInsuranceDto { Available = false, Error = "Registration not found or access denied." };
            }

            var jobId = clubRepReg.JobId;

            var jobOffer = await _jobRepo.GetInsuranceOfferInfoAsync(jobId);
            if (jobOffer == null || !jobOffer.BOfferTeamRegsaverInsurance)
            {
                return new PreSubmitTeamInsuranceDto { Available = false };
            }

            var teams = await _teamRepo.GetRegisteredTeamsForPaymentAsync(jobId, regId);
            if (teams.Count == 0)
            {
                return new PreSubmitTeamInsuranceDto { Available = false };
            }

            // Get club rep user profile for customer data
            var clubRepUser = await _userRepo.GetByIdAsync(userId);
            if (clubRepUser == null)
            {
                return new PreSubmitTeamInsuranceDto { Available = false, Error = "Club rep user not found." };
            }

            var director = await _registrationRepo.GetDirectorContactForJobAsync(jobId);
            var products = BuildTeamProducts(teams, clubRepUser, director, jobOffer.JobName);
            var teamObj = BuildTeamObject(products);

            return new PreSubmitTeamInsuranceDto
            {
                Available = true,
                TeamObject = teamObj,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                StateId = $"vi-team-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VerticalInsure] Failed to build team offer.");
            return new PreSubmitTeamInsuranceDto { Available = false, Error = "Team offer generation failed." };
        }
    }

    public async Task<VerticalInsureTeamPurchaseResult> PurchaseTeamPoliciesAsync(
        Guid regId,
        string userId,
        IReadOnlyCollection<Guid> teamIds,
        IReadOnlyCollection<string> quoteIds,
        string? token,
        CreditCardInfo? card,
        CancellationToken ct = default)
    {
        try
        {
            // Get club rep registration to derive jobId
            var registrations = await _registrationRepo.GetByIdsAsync([regId]);
            var clubRepReg = registrations.FirstOrDefault();
            if (clubRepReg == null || clubRepReg.UserId != userId)
            {
                return new VerticalInsureTeamPurchaseResult
                {
                    Success = false,
                    Error = "Registration not found or access denied.",
                    Policies = new()
                };
            }

            var jobId = clubRepReg.JobId;
            var (isValid, validationError, teams) = await ValidateAndLoadTeamsAsync(jobId, teamIds, quoteIds, ct);
            if (!isValid)
            {
                return new VerticalInsureTeamPurchaseResult
                {
                    Success = false,
                    Error = validationError,
                    Policies = new()
                };
            }

            if (_httpClientFactory != null)
            {
                return await ExecuteTeamHttpPurchaseAsync(teams, regId, quoteIds, token, card, ct);
            }
            else
            {
                return await ApplyTeamStubPurchaseAsync(teams, regId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VerticalInsure] Team insurance purchase failed.");
            return new VerticalInsureTeamPurchaseResult
            {
                Success = false,
                Error = "Team insurance purchase failed.",
                Policies = new()
            };
        }
    }

    private async Task<(bool isValid, string? error, List<TeamEntity> teams)> ValidateAndLoadTeamsAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        IReadOnlyCollection<string> quoteIds,
        CancellationToken ct)
    {
        if (teamIds.Count == 0 && quoteIds.Count == 0)
        {
            return (false, "No teams and no quotes supplied.", new());
        }
        if (teamIds.Count == 0)
        {
            return (false, "No team IDs supplied.", new());
        }
        if (quoteIds.Count == 0)
        {
            return (false, "No insurance quote IDs supplied.", new());
        }
        if (teamIds.Count != quoteIds.Count)
        {
            return (false, "Team / quote count mismatch.", new());
        }

        var teams = await _teamRepo.GetTeamsForJobAsync(jobId, teamIds, ct);
        if (teams.Count == 0)
        {
            return (false, "No matching teams found.", new());
        }
        if (teams.Exists(t => !string.IsNullOrWhiteSpace(t.ViPolicyId)))
        {
            return (false, "One or more teams already have an insurance policy.", new());
        }

        return (true, null, teams);
    }

    private async Task<VerticalInsureTeamPurchaseResult> ExecuteTeamHttpPurchaseAsync(
        List<TeamEntity> teams,
        Guid clubRepRegId,
        IReadOnlyCollection<string> quoteIds,
        string? token,
        CreditCardInfo? card,
        CancellationToken ct)
    {
        var client = _httpClientFactory!.CreateClient("verticalinsure");
        var (clientId, clientSecret) = ResolveCredentials();
        var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        var payload = BuildBatchPayload(quoteIds, token, card);
        var req = new HttpRequestMessage(HttpMethod.Post, "v1/purchase/team-registration/batch")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Authorization", $"Basic {authString}");
        req.Headers.Add("User-Agent", "TSIC.API HttpClient");

        var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode ||
            !(response.StatusCode == System.Net.HttpStatusCode.Created || response.StatusCode == System.Net.HttpStatusCode.OK))
        {
            return new VerticalInsureTeamPurchaseResult
            {
                Success = false,
                Error = $"Team insurance purchase HTTP error: {(int)response.StatusCode}",
                Policies = new()
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var policies = await JsonSerializer.DeserializeAsync<List<VIMakeTeamPaymentResponseDto>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            ct) ?? new();

        var policyDict = new Dictionary<Guid, string>();

        foreach (var policy in policies)
        {
            if (policy.policy_status == "ACTIVE" && !string.IsNullOrWhiteSpace(policy.policy_number))
            {
                var team = teams.Find(t => t.TeamId == policy.metadata.tsic_teamid);
                if (team != null)
                {
                    team.ViPolicyId = policy.policy_number;
                    team.ViPolicyCreateDate = DateTime.UtcNow;
                    team.ViPolicyClubRepRegId = clubRepRegId;
                    team.Modified = DateTime.UtcNow;
                    policyDict[team.TeamId] = policy.policy_number;
                }
            }
        }

        await _teamRepo.SaveChangesAsync(ct);

        return new VerticalInsureTeamPurchaseResult
        {
            Success = true,
            Error = null,
            Policies = policyDict
        };
    }

    private async Task<VerticalInsureTeamPurchaseResult> ApplyTeamStubPurchaseAsync(
        IEnumerable<TeamEntity> teams,
        Guid clubRepRegId,
        CancellationToken ct)
    {
        var policyDict = new Dictionary<Guid, string>();

        foreach (var team in teams)
        {
            var policyNo = $"TPOL-{team.TeamId.ToString("N").Substring(0, 8).ToUpper()}";
            team.ViPolicyId = policyNo;
            team.ViPolicyCreateDate = DateTime.UtcNow;
            team.ViPolicyClubRepRegId = clubRepRegId;
            team.Modified = DateTime.UtcNow;
            policyDict[team.TeamId] = policyNo;
        }

        await _teamRepo.SaveChangesAsync(ct);

        return new VerticalInsureTeamPurchaseResult
        {
            Success = true,
            Error = null,
            Policies = policyDict
        };
    }

    private List<VITeamProductDto> BuildTeamProducts(
        List<RegisteredTeamInfo> teams,
        AspNetUsers clubRepUser,
        DirectorContactInfo? director,
        string? jobName)
    {
        var products = new List<VITeamProductDto>();
        var contextName = (jobName ?? string.Empty).Split(':')[0];

        foreach (var team in teams)
        {
            var product = new VITeamProductDto
            {
                customer = new VICustomerDto
                {
                    email_address = clubRepUser.Email ?? string.Empty,
                    first_name = clubRepUser.FirstName ?? string.Empty,
                    last_name = clubRepUser.LastName ?? string.Empty,
                    city = clubRepUser.City ?? string.Empty,
                    state = clubRepUser.State ?? string.Empty,
                    postal_code = clubRepUser.PostalCode ?? string.Empty,
                    phone = clubRepUser.Cellphone ?? string.Empty,
                    street = string.Empty
                },
                metadata = new VITeamMetadataDto
                {
                    tsic_secondchance = "0",
                    context_event = jobName ?? contextName,
                    context_name = contextName,
                    context_description = team.TeamName,
                    tsic_teamid = team.TeamId
                },
                policy_attributes = new VITeamPolicyAttributes
                {
                    organization_name = director?.OrgName ?? string.Empty,
                    organization_contact_name = $"{director?.FirstName} {director?.LastName}".Trim(),
                    organization_contact_email = director?.Email ?? string.Empty,
                    teams = new List<VITeamDto>
                    {
                        new VITeamDto
                        {
                            team_name = team.TeamName,
                            insurable_amount = (int)(team.FeeTotal * 100)
                        }
                    },
                    job_event = new VIEventDto
                    {
                        name = jobName ?? contextName,
                        type = "Tournament",
                        location = director?.OrgName ?? string.Empty,
                        address = new VIAddress
                        {
                            city = string.Empty,
                            state = string.Empty,
                            zip = string.Empty,
                            street = string.Empty
                        },
                        event_start_date = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd"),
                        event_end_date = DateTime.Now.AddYears(1).ToString("yyyy-MM-dd")
                    }
                }
            };
            products.Add(product);
        }
        return products;
    }

    private VITeamObjectResponse BuildTeamObject(List<VITeamProductDto> products)
    {
        const string DEV_CLIENT_ID = "test_GREVHKFHJY87CGWW9RF15JD50W5PPQ7U";
        const string PROD_CLIENT_ID = "live_VJ8O8O81AZQ8MCSKWM98928597WUHSMS";
        var clientId = _env.IsDevelopment() ? DEV_CLIENT_ID : PROD_CLIENT_ID;

        return new VITeamObjectResponse
        {
            client_id = clientId,
            payments = new VIPaymentsDto { enabled = false, button = false },
            theme = new VIThemeDto
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
            product_config = new VITeamProductConfigDto
            {
                team_registration = products
            }
        };
    }
}
