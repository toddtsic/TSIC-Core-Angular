using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TSIC.API.Extensions;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Dtos.VerticalInsure;
using TSIC.Contracts.Extensions;
using TSIC.Domain.Entities;
using TSIC.Domain.Constants;
using TSIC.Application.Services.Shared.Insurance;
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

            // VI's team-registration product rejects quotes within 14 days of event
            // start, and we must send a real start date (not a placeholder) so the
            // window check matches the tournament. Without EventStartDate configured
            // there is nothing to send — silently skip the offer.
            if (jobOffer.EventStartDate == null)
            {
                return new PreSubmitTeamInsuranceDto { Available = false };
            }

            // Don't surface an offer the carrier will reject. VI's 14-day cutoff is
            // measured against EventStartDate; mirror it here so reps don't see a
            // widget that would 400 on quote.
            if (jobOffer.EventStartDate.Value.Date < DateTime.Now.Date.AddDays(14))
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

            // The event address comes from the event's own field data, never from whoever
            // happens to be director -- an AspNetUsers home or office address on a weather
            // policy misprices the risk and can void a payout. Legacy read the same rows
            // (IRegistrationService.cs:1397) and refused the offer without them; so do we.
            // Fail CLOSED: no offer beats a quote that 400s on an empty address.
            var eventLocation = await ResolveEventLocationAsync(jobId);
            if (eventLocation is null || !eventLocation.HasCompleteAddress)
            {
                _logger.LogWarning(
                    "[VerticalInsure] Team offer suppressed for job {JobId}: no attached field "
                    + "carries a complete event address (candidate: {Candidate}).",
                    jobId, eventLocation?.FName ?? "none");
                return new PreSubmitTeamInsuranceDto
                {
                    Available = false,
                    Error = "This event does not yet have a location on file. Team insurance "
                          + "cannot be offered until the event organizer adds one."
                };
            }

            var director = await _registrationRepo.GetDirectorContactForJobAsync(jobId);
            var products = await BuildTeamProductsAsync(teams, regId, clubRepUser, clubRepReg.ClubName, director, eventLocation, jobOffer.JobName, jobOffer.EventStartDate.Value, jobOffer.EventEndDate);
            var teamObj = BuildTeamObject(products);

            return new PreSubmitTeamInsuranceDto
            {
                Available = true,
                TeamObject = teamObj,
                ExpiresUtc = DateTime.Now.AddMinutes(10),
                StateId = $"vi-team-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}",
                JobUsesAmex = await _paymentFeatures.UsesAmexAsync(jobId)
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
                var team = policy.metadata != null
                    ? teams.Find(t => t.TeamId == policy.metadata.tsic_teamid)
                    : null;
                if (team != null)
                {
                    team.ViPolicyId = policy.policy_number;
                    team.ViPolicyCreateDate = DateTime.Now;
                    team.ViPolicyClubRepRegId = clubRepRegId;
                    team.Modified = DateTime.Now;
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
            team.ViPolicyCreateDate = DateTime.Now;
            team.ViPolicyClubRepRegId = clubRepRegId;
            team.Modified = DateTime.Now;
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

    /// <summary>
    /// The one field row whose address represents this event, or null when the event has no
    /// location on file.
    ///
    /// This is THE resolver. Manage Fields calls EventLocationFieldNaming.SelectEventLocation
    /// with the same inputs to decide which row it badges as the event location, so what a
    /// director is told is insurable and what actually reaches Vertical Insure cannot drift.
    /// Change the rule in EventLocationFieldNaming and both move together.
    /// </summary>
    private async Task<EventLocationCandidateDto?> ResolveEventLocationAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var jobPath = await _jobRepo.GetJobPathAsync(jobId, ct);
        var candidates = await _fieldRepo.GetEventLocationCandidatesAsync(jobId, ct);
        return EventLocationFieldNaming.SelectEventLocation(candidates, c => c.FName, jobPath);
    }

    private async Task<List<VITeamProductDto>> BuildTeamProductsAsync(
        List<RegisteredTeamInfo> teams,
        Guid clubRepRegId,
        AspNetUsers clubRepUser,
        string? clubRepClubName,
        DirectorContactInfo? director,
        EventLocationCandidateDto eventLocation,
        string? jobName,
        DateTime eventStartDate,
        DateTime? eventEndDate)
    {
        var products = new List<VITeamProductDto>();

        // Job names are "Org:Event". Split once and the two halves land in the two places VI
        // asks for them -- organization_name and event.name -- instead of the composite going
        // into both. A name with no colon is all event, and the organization repeats it.
        var fullName = jobName ?? string.Empty;
        var colonIndex = fullName.IndexOf(':');
        var contextName = (colonIndex >= 0 ? fullName[..colonIndex] : fullName).Trim();
        var eventName = (colonIndex >= 0 ? fullName[(colonIndex + 1)..] : fullName).Trim();
        if (eventName.Length == 0) eventName = contextName;
        if (contextName.Length == 0) contextName = eventName;

        foreach (var team in teams)
        {
            // Full configured price (deposit + balance), phase-independent — insure the whole
            // forfeitable team-registration cost, not just the current-phase (deposit) base.
            // Fall back to the stamped phase base only when the cascade is unconfigured (an
            // anomaly: the repo pre-filter already requires FeeTotal > 0).
            var fullPrice = await _teamLookupService.ResolveFullPriceAsync(team.TeamId, RoleConstants.ClubRep);
            var baseFee = fullPrice > 0m ? fullPrice : team.FeeBase;

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
                    street = clubRepUser.StreetAddress ?? string.Empty
                },
                metadata = new VITeamMetadataDto
                {
                    tsic_secondchance = "0",
                    context_event = jobName ?? contextName,
                    context_name = contextName,
                    context_description = team.TeamName,
                    tsic_clubname = clubRepClubName ?? string.Empty,
                    // Was never set, so every team policy went out keyed to Guid.Empty and could
                    // not be traced back to the purchase. Legacy stamped it
                    // (IRegistrationService.cs:1507).
                    tsic_registrationid = clubRepRegId,
                    tsic_teamid = team.TeamId
                },
                policy_attributes = new VITeamPolicyAttributes
                {
                    // Two parties, two places. The PURCHASER is the club rep and rides in
                    // customer above. The EVENT is described here: its address off the field
                    // row, and its organization -- the director who runs it, whom Vertical
                    // Insure has to be able to reach on a cancellation. The club rep is already
                    // fully contactable as the customer, so spending this slot on them would
                    // duplicate the purchaser and leave VI no route to the organizer.
                    // Matches the player payload, which has always sent the director here.
                    // contextName, not director.OrgName: OrgName is literally Job.JobName
                    // (RegistrationRepository.cs:980), so preferring it named the organization
                    // "Top Threat Tournaments:Merry Laxmas North". Job names are "Org:Event"
                    // and the organization is the half before the colon -- the same value
                    // metadata.context_name already carries.
                    organization_name = contextName,
                    organization_contact_name = $"{director?.FirstName} {director?.LastName}".Trim(),
                    organization_contact_email = director?.Email ?? string.Empty,
                    teams = new List<VITeamDto>
                    {
                        new VITeamDto
                        {
                            team_name = team.TeamName,
                            // Reflect the per-team modifiers: minus EVERY stamped discount
                            // (TotalDiscount() = early-bird/discount-code + multi-player), plus late
                            // fees (processing surcharge and donation excluded).
                            insurable_amount = InsurableAmountCalculator.ComputeNetInsurableAmount(
                                baseFee, team.TotalDiscount(), team.FeeLatefee)
                        }
                    },
                    job_event = new VIEventDto
                    {
                        name = eventName,
                        type = "Tournament",
                        // Legacy sent the event STATE here, not an organization name
                        // (IRegistrationService.cs:1512).
                        location = eventLocation.State ?? string.Empty,
                        // The event's own address, off the attached field row. Every part is
                        // non-empty -- the caller refuses the offer otherwise, because empty
                        // strings make VI's team-registration endpoint 400 on "Invalid zip code".
                        address = new VIAddress
                        {
                            city = eventLocation.City ?? string.Empty,
                            state = eventLocation.State ?? string.Empty,
                            zip = eventLocation.Zip ?? string.Empty,
                            street = eventLocation.Address ?? string.Empty
                        },
                        // ISO 8601 sortable to match legacy `$"{job.EventStartDate:s}"`.
                        // VI's team-registration product enforces start ≥ 14 days from
                        // today; null is filtered out upstream in BuildTeamOfferAsync.
                        event_start_date = $"{eventStartDate:s}",
                        event_end_date = eventEndDate.HasValue
                            ? $"{eventEndDate.Value:s}"
                            : eventStartDate.AddYears(1).ToString("s")
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
        var clientId = _env.IsSandbox() ? DEV_CLIENT_ID : PROD_CLIENT_ID;

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
