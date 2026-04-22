using Microsoft.Extensions.Logging;
using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Dtos.UsLax;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Admin;

public sealed class UsLaxMembershipService : IUsLaxMembershipService
{
    private readonly IRegistrationRepository _registrations;
    private readonly IUsLaxService _usLax;
    private readonly ILogger<UsLaxMembershipService> _logger;

    public UsLaxMembershipService(
        IRegistrationRepository registrations,
        IUsLaxService usLax,
        ILogger<UsLaxMembershipService> logger)
    {
        _registrations = registrations;
        _usLax = usLax;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UsLaxReconciliationCandidateDto>> GetCandidatesAsync(Guid jobId, CancellationToken ct = default)
    {
        var rows = await _registrations.GetUsLaxReconciliationCandidatesAsync(jobId, ct);
        return rows.Select(r => new UsLaxReconciliationCandidateDto
        {
            RegistrationId = r.RegistrationId,
            FirstName = r.FirstName,
            LastName = r.LastName,
            Dob = r.Dob,
            MembershipId = r.SportAssnId,
            CurrentExpiryDate = r.SportAssnIdexpDate,
            TeamName = r.TeamName
        }).ToList();
    }

    public async Task<UsLaxReconciliationResponse> ReconcileAsync(Guid jobId, UsLaxReconciliationRequest request, CancellationToken ct = default)
    {
        var candidates = await _registrations.GetUsLaxReconciliationCandidatesAsync(jobId, ct);

        if (request.RegistrationIds is { Count: > 0 })
        {
            var filter = request.RegistrationIds.ToHashSet();
            candidates = candidates.Where(c => filter.Contains(c.RegistrationId)).ToList();
        }

        var rows = new List<UsLaxReconciliationRowDto>(candidates.Count);
        var datesUpdated = 0;
        var failed = 0;

        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var row = await ReconcileOneAsync(c, ct);
            rows.Add(row);
            if (row.ExpiryDateUpdated) datesUpdated++;
            if (row.StatusCode != 200) failed++;
        }

        return new UsLaxReconciliationResponse
        {
            TotalPinged = rows.Count,
            DatesUpdated = datesUpdated,
            Failed = failed,
            Rows = rows
        };
    }

    private async Task<UsLaxReconciliationRowDto> ReconcileOneAsync(UsLaxReconciliationCandidateRow c, CancellationToken ct)
    {
        UsLaxMemberPingResult? ping;
        try
        {
            ping = await _usLax.GetMemberAsync(c.SportAssnId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USLax ping failed for registration {RegistrationId} / member {MembershipId}", c.RegistrationId, c.SportAssnId);
            ping = null;
        }

        if (ping == null)
        {
            return BuildRow(c, statusCode: 0, errorMessage: "Network or parse failure", newExpiry: null, updated: false);
        }

        if (ping.StatusCode != 200 || ping.Output is null)
        {
            return BuildRow(c, statusCode: ping.StatusCode, errorMessage: ping.ErrorMessage, newExpiry: null, updated: false, output: ping.Output);
        }

        var output = ping.Output;
        DateTime? newExpiry = null;
        var updated = false;

        // Legacy rule: write back when USALax returns an exp_date AND involvement includes "Player".
        // Skip no-op writes when the new date matches what's already on file.
        var isPlayer = output.Involvement?.Any(s => s.Equals("Player", StringComparison.OrdinalIgnoreCase)) == true;
        if (isPlayer && DateTime.TryParse(output.ExpDate, out var parsed))
        {
            newExpiry = parsed.Date;
            if (c.SportAssnIdexpDate?.Date != newExpiry)
            {
                try
                {
                    await _registrations.UpdateSportAssnIdExpDateAsync(c.RegistrationId, newExpiry.Value, ct);
                    updated = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write SportAssnIdexpDate for registration {RegistrationId}", c.RegistrationId);
                }
            }
        }

        return BuildRow(c, statusCode: 200, errorMessage: null, newExpiry: newExpiry, updated: updated, output: output);
    }

    private static UsLaxReconciliationRowDto BuildRow(
        UsLaxReconciliationCandidateRow c,
        int statusCode,
        string? errorMessage,
        DateTime? newExpiry,
        bool updated,
        UsLaxMemberPingOutput? output = null)
    {
        return new UsLaxReconciliationRowDto
        {
            RegistrationId = c.RegistrationId,
            FirstName = c.FirstName,
            LastName = c.LastName,
            MembershipId = c.SportAssnId,
            TeamName = c.TeamName,
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            MemStatus = output?.MemStatus,
            AgeVerified = output?.AgeVerified,
            Involvement = output?.Involvement,
            PreviousExpiryDate = c.SportAssnIdexpDate,
            NewExpiryDate = newExpiry ?? c.SportAssnIdexpDate,
            ExpiryDateUpdated = updated
        };
    }
}
