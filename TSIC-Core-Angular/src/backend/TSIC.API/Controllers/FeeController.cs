using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Fees;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Teams;

namespace TSIC.API.Controllers;

/// <summary>
/// Fee management endpoints for LADT admin UI.
/// Reads/writes from fees.JobFees + fees.FeeModifiers.
/// </summary>
[ApiController]
[Route("api/fees")]
[Authorize]
public class FeeController : ControllerBase
{
    private readonly IFeeRepository _feeRepo;
    private readonly IJobLookupService _jobLookup;
    private readonly IPlayerRegistrationService _playerRegService;
    private readonly ITeamRegistrationService _teamRegService;
    private readonly IAgeGroupRepository _ageGroups;

    public FeeController(
        IFeeRepository feeRepo,
        IJobLookupService jobLookup,
        IPlayerRegistrationService playerRegService,
        ITeamRegistrationService teamRegService,
        IAgeGroupRepository ageGroups)
    {
        _feeRepo = feeRepo;
        _jobLookup = jobLookup;
        _playerRegService = playerRegService;
        _teamRegService = teamRegService;
        _ageGroups = ageGroups;
    }

    private async Task<Guid> ResolveJobIdAsync()
    {
        return await User.GetJobIdFromRegistrationAsync(_jobLookup)
            ?? throw new UnauthorizedAccessException("Unable to resolve job from registration.");
    }

    /// <summary>
    /// Get all fee rows for an agegroup (agegroup-level + team-level overrides).
    /// </summary>
    [HttpGet("agegroup/{agegroupId:guid}")]
    public async Task<ActionResult<List<JobFeeDto>>> GetAgegroupFees(
        Guid agegroupId, CancellationToken ct)
    {
        var jobId = await ResolveJobIdAsync();
        var rows = await _feeRepo.GetJobFeesByAgegroupAsync(jobId, agegroupId, ct);
        return Ok(rows.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Get the league-scoped fee rows for a league (league-level early-bird/late-fee).
    /// </summary>
    [HttpGet("league/{leagueId:guid}")]
    public async Task<ActionResult<List<JobFeeDto>>> GetLeagueFees(
        Guid leagueId, CancellationToken ct)
    {
        var jobId = await ResolveJobIdAsync();
        var rows = await _feeRepo.GetJobFeesByLeagueAsync(jobId, leagueId, ct);
        return Ok(rows.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Get all fee rows for the entire job (all roles, all scopes).
    /// </summary>
    [HttpGet("job")]
    public async Task<ActionResult<List<JobFeeDto>>> GetJobFees(CancellationToken ct)
    {
        var jobId = await ResolveJobIdAsync();
        var rows = await _feeRepo.GetJobFeesByJobAsync(jobId, ct);
        return Ok(rows.Select(MapToDto).ToList());
    }

    /// <summary>
    /// The "blast area" for a pending fee/phase change at a scope: how many existing
    /// registrations a save would reprice. Player rows → active player registrations in scope;
    /// ClubRep rows → eligible teams in scope. Mirrors the canonical reprice engines' selection
    /// so the number matches what a subsequent <see cref="SaveFee"/> would actually touch.
    /// The frontend calls this before confirming a money change to inform the admin (and to
    /// gate the prompt — 0 means save silently). Scope precedence: team → agegroup → league →
    /// whole job; league expands to its agegroups.
    /// </summary>
    [HttpGet("affected-count")]
    public async Task<ActionResult<AffectedRegistrationCountDto>> GetAffectedCount(
        [FromQuery] string roleId,
        [FromQuery] Guid? leagueId,
        [FromQuery] Guid? agegroupId,
        [FromQuery] Guid? teamId,
        CancellationToken ct)
    {
        var jobId = await ResolveJobIdAsync();

        // Map the fee card's scope to the agegroup id-set the engines filter on. Team scope
        // filters by team directly (most specific); agegroup scope is a single id; league scope
        // expands to the league's agegroups; no ids = whole job.
        IReadOnlyCollection<Guid>? agegroupIds = null;
        if (!teamId.HasValue)
        {
            if (agegroupId.HasValue)
            {
                agegroupIds = new[] { agegroupId.Value };
            }
            else if (leagueId.HasValue)
            {
                var agegroups = await _ageGroups.GetByLeagueIdAsync(leagueId.Value, ct);
                agegroupIds = agegroups.Select(a => a.AgegroupId).ToList();
            }
        }

        var count = roleId == RoleConstants.ClubRep
            ? await _teamRegService.CountEligibleTeamsInScopeAsync(jobId, agegroupIds, teamId)
            : await _playerRegService.CountActivePlayersInScopeAsync(jobId, agegroupIds, teamId, ct);

        return Ok(new AffectedRegistrationCountDto { Count = count });
    }

    /// <summary>
    /// Create or update a fee row for a role at a scope.
    /// If a row already exists for this (Job, Role, Agegroup, Team), it's updated.
    /// If not, a new row is created.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<SaveJobFeeResponse>> SaveFee(
        [FromBody] SaveJobFeeRequest request, CancellationToken ct)
    {
        var jobId = await ResolveJobIdAsync();
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Guard: an Early Bird Discount and a Late Fee on the same fee card must
        // not have overlapping date windows. A registrant active in the overlap
        // would be stamped BOTH (feeAdj = lateFee − discount), which is never intended.
        var windowError = ValidateModifierWindows(request.Modifiers);
        if (windowError != null)
            return BadRequest(new { message = windowError });

        // Find existing row for this scope (tracked for update)
        var existing = await _feeRepo.GetTrackedByScopeAsync(
            jobId, request.RoleId, request.AgegroupId, request.TeamId, request.LeagueId, ct);

        // Phase changes are always retroactive — snapshot the prior value so we can force a
        // reprice on any flip (on/off), even when the caller didn't tick "update all prior".
        var prevPhase = existing?.BFullPaymentRequired;

        JobFees row;
        if (existing != null)
        {
            existing.Deposit = request.Deposit;
            existing.BalanceDue = request.BalanceDue;
            existing.BFullPaymentRequired = request.BFullPaymentRequired;
            existing.Modified = DateTime.Now;
            existing.LebUserId = userId;
            SyncModifiers(existing, request.Modifiers, userId);
            row = existing;
        }
        else
        {
            row = new JobFees
            {
                JobFeeId = Guid.NewGuid(),
                JobId = jobId,
                RoleId = request.RoleId,
                AgegroupId = request.AgegroupId,
                TeamId = request.TeamId,
                LeagueId = request.LeagueId,
                Deposit = request.Deposit,
                BalanceDue = request.BalanceDue,
                BFullPaymentRequired = request.BFullPaymentRequired,
                Modified = DateTime.Now,
                LebUserId = userId
            };
            _feeRepo.Add(row);

            if (request.Modifiers != null)
            {
                foreach (var mod in request.Modifiers)
                {
                    _feeRepo.AddModifier(new FeeModifiers
                    {
                        FeeModifierId = Guid.NewGuid(),
                        JobFeeId = row.JobFeeId,
                        ModifierType = mod.ModifierType,
                        Amount = mod.Amount,
                        StartDate = mod.StartDate,
                        EndDate = mod.EndDate,
                        Modified = DateTime.Now,
                        LebUserId = userId
                    });
                }
            }
        }

        await _feeRepo.SaveChangesAsync(ct);

        // Retroactive reprice: explicitly requested ("update all prior") OR forced by a
        // phase flip (always retroactive). Only Player/ClubRep rows drive registration
        // fees; other roles have nothing to reprice. Canonical scoped engines only.
        var phaseChanged = prevPhase != request.BFullPaymentRequired;
        var repriced = 0;
        if ((request.RepriceExisting || phaseChanged)
            && (request.RoleId == RoleConstants.Player || request.RoleId == RoleConstants.ClubRep))
        {
            repriced = await DispatchRepriceAsync(jobId, userId, request, ct);
        }

        return Ok(new SaveJobFeeResponse { Fee = MapToDto(row), RegistrationsRepriced = repriced });
    }

    /// <summary>
    /// Dispatches a scoped reprice of existing registrations after a fee/phase save, using
    /// the canonical recalc engines (never bespoke fee math). Player rows → player reprice
    /// scoped to the card's agegroup/team; ClubRep rows → team reprice (which also rolls the
    /// club-rep registration row up). Agegroup/league-scoped team repricing falls back to the
    /// whole job — only in-scope teams actually change; the rest recompute to the same value.
    /// </summary>
    private async Task<int> DispatchRepriceAsync(
        Guid jobId, string? userId, SaveJobFeeRequest request, CancellationToken ct)
    {
        // Stamp the audit user; fall back to SuperUser (a real AspNetUsers FK) if the claim
        // is somehow absent, matching JobConfigService's recalc trigger.
        var actor = userId ?? TsicConstants.SuperUserId;

        if (request.RoleId == RoleConstants.Player)
        {
            return await _playerRegService.RecalculatePlayerFeesAsync(
                jobId, actor, request.AgegroupId, request.TeamId, ct);
        }

        var teamRequest = request.TeamId.HasValue
            ? new RecalculateTeamFeesRequest { TeamId = request.TeamId.Value }
            : new RecalculateTeamFeesRequest { JobId = jobId };
        var result = await _teamRegService.RecalculateTeamFeesAsync(teamRequest, actor);
        return result.UpdatedCount;
    }

    /// <summary>
    /// Delete a fee row (and its modifiers via cascade).
    /// </summary>
    [HttpDelete("{jobFeeId:guid}")]
    public async Task<ActionResult> DeleteFee(Guid jobFeeId, CancellationToken ct)
    {
        var jobId = await ResolveJobIdAsync();
        var row = await _feeRepo.GetTrackedByIdAsync(jobFeeId, ct);

        if (row == null || row.JobId != jobId)
            return NotFound();

        _feeRepo.Remove(row);
        await _feeRepo.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ──

    /// <summary>
    /// Rejects a save where an Early Bird Discount window overlaps a Late Fee window
    /// on the same fee card. Null start = open-ended past, null end = open-ended future;
    /// boundaries are inclusive (both active on a shared boundary date counts as overlap).
    /// Returns an error message, or null when valid.
    /// </summary>
    private static string? ValidateModifierWindows(List<FeeModifierDto>? modifiers)
    {
        if (modifiers == null) return null;

        var earlyBirds = modifiers.Where(m => m.ModifierType == FeeConstants.ModifierEarlyBird).ToList();
        var lateFees = modifiers.Where(m => m.ModifierType == FeeConstants.ModifierLateFee).ToList();
        if (earlyBirds.Count == 0 || lateFees.Count == 0) return null;

        foreach (var eb in earlyBirds)
            foreach (var lf in lateFees)
                if (WindowsOverlap(eb.StartDate, eb.EndDate, lf.StartDate, lf.EndDate))
                    return "Early Bird Discount and Late Fee date windows overlap. A registrant "
                         + "active in the overlap would receive both — set the Early Bird to end "
                         + "before the Late Fee begins.";

        return null;
    }

    private static bool WindowsOverlap(DateTime? aStart, DateTime? aEnd, DateTime? bStart, DateTime? bEnd)
    {
        var s1 = aStart ?? DateTime.MinValue;
        var e1 = aEnd ?? DateTime.MaxValue;
        var s2 = bStart ?? DateTime.MinValue;
        var e2 = bEnd ?? DateTime.MaxValue;
        return s1 <= e2 && s2 <= e1;
    }

    private void SyncModifiers(JobFees row, List<FeeModifierDto>? requested, string? userId)
    {
        var existingModifiers = row.FeeModifiers?.ToList() ?? new();
        var requestedIds = requested?
            .Where(m => m.FeeModifierId.HasValue)
            .Select(m => m.FeeModifierId!.Value)
            .ToHashSet() ?? new HashSet<Guid>();

        // Remove modifiers not in request
        foreach (var existing in existingModifiers)
        {
            if (!requestedIds.Contains(existing.FeeModifierId))
                _feeRepo.RemoveModifier(existing);
        }

        if (requested == null) return;

        foreach (var mod in requested)
        {
            if (mod.FeeModifierId.HasValue)
            {
                var existing = existingModifiers.FirstOrDefault(m => m.FeeModifierId == mod.FeeModifierId.Value);
                if (existing != null)
                {
                    existing.ModifierType = mod.ModifierType;
                    existing.Amount = mod.Amount;
                    existing.StartDate = mod.StartDate;
                    existing.EndDate = mod.EndDate;
                    existing.Modified = DateTime.Now;
                    existing.LebUserId = userId;
                }
            }
            else
            {
                _feeRepo.AddModifier(new FeeModifiers
                {
                    FeeModifierId = Guid.NewGuid(),
                    JobFeeId = row.JobFeeId,
                    ModifierType = mod.ModifierType,
                    Amount = mod.Amount,
                    StartDate = mod.StartDate,
                    EndDate = mod.EndDate,
                    Modified = DateTime.Now,
                    LebUserId = userId
                });
            }
        }
    }

    private static JobFeeDto MapToDto(JobFees jf) => new()
    {
        JobFeeId = jf.JobFeeId,
        JobId = jf.JobId,
        RoleId = jf.RoleId,
        AgegroupId = jf.AgegroupId,
        TeamId = jf.TeamId,
        LeagueId = jf.LeagueId,
        Deposit = jf.Deposit,
        BalanceDue = jf.BalanceDue,
        BFullPaymentRequired = jf.BFullPaymentRequired,
        Modifiers = jf.FeeModifiers?.Select(m => new FeeModifierDto
        {
            FeeModifierId = m.FeeModifierId,
            ModifierType = m.ModifierType,
            Amount = m.Amount,
            StartDate = m.StartDate,
            EndDate = m.EndDate
        }).ToList()
    };
}
