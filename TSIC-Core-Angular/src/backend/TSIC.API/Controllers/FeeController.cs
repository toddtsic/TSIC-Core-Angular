using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.Contracts.Dtos.Fees;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;

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

    public FeeController(IFeeRepository feeRepo, IJobLookupService jobLookup)
    {
        _feeRepo = feeRepo;
        _jobLookup = jobLookup;
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
    /// Create or update a fee row for a role at a scope.
    /// If a row already exists for this (Job, Role, Agegroup, Team), it's updated.
    /// If not, a new row is created.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<JobFeeDto>> SaveFee(
        [FromBody] SaveJobFeeRequest request, CancellationToken ct)
    {
        var jobId = await ResolveJobIdAsync();
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Find existing row for this scope (tracked for update)
        var existing = await _feeRepo.GetTrackedByScopeAsync(
            jobId, request.RoleId, request.AgegroupId, request.TeamId, ct);

        JobFees row;
        if (existing != null)
        {
            existing.Deposit = request.Deposit;
            existing.BalanceDue = request.BalanceDue;
            existing.Modified = DateTime.UtcNow;
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
                Deposit = request.Deposit,
                BalanceDue = request.BalanceDue,
                Modified = DateTime.UtcNow,
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
                        Modified = DateTime.UtcNow,
                        LebUserId = userId
                    });
                }
            }
        }

        await _feeRepo.SaveChangesAsync(ct);
        return Ok(MapToDto(row));
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
                    existing.Modified = DateTime.UtcNow;
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
                    Modified = DateTime.UtcNow,
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
        Deposit = jf.Deposit,
        BalanceDue = jf.BalanceDue,
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
