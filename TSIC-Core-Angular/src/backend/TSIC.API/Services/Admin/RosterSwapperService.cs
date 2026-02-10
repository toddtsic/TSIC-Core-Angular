using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.API.Services.Players;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for the Roster Swapper admin tool.
/// Implements four distinct transfer flows based on registration role:
/// 1. Player → Team: standard swap (UPDATE AssignedTeamId + fee recalc)
/// 2. Unassigned Adult → Team: CREATE new Staff registration
/// 3. Staff → Unassigned pool: DELETE Staff registration
/// 4. Staff → Team: UPDATE AssignedTeamId (no fee recalc)
/// </summary>
public sealed class RosterSwapperService : IRosterSwapperService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IRegistrationRecordFeeCalculatorService _feeCalc;

    public RosterSwapperService(
        IRegistrationRepository registrationRepo,
        ITeamRepository teamRepo,
        IDeviceRepository deviceRepo,
        IRegistrationRecordFeeCalculatorService feeCalc)
    {
        _registrationRepo = registrationRepo;
        _teamRepo = teamRepo;
        _deviceRepo = deviceRepo;
        _feeCalc = feeCalc;
    }

    public async Task<List<SwapperPoolOptionDto>> GetPoolOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _teamRepo.GetSwapperPoolOptionsAsync(jobId, ct);
    }

    public async Task<List<SwapperPlayerDto>> GetRosterAsync(Guid poolId, Guid jobId, CancellationToken ct = default)
    {
        if (poolId == Guid.Empty)
            return await _registrationRepo.GetUnassignedAdultsAsync(jobId, ct);

        // Validate team belongs to job
        if (!await _teamRepo.BelongsToJobAsync(poolId, jobId, ct))
            throw new ArgumentException("Team does not belong to this job.");

        return await _registrationRepo.GetRosterByTeamIdAsync(poolId, jobId, ct);
    }

    public async Task<List<RosterTransferFeePreviewDto>> PreviewTransferAsync(
        Guid jobId, RosterTransferPreviewRequest request, CancellationToken ct = default)
    {
        if (request.SourcePoolId == request.TargetPoolId)
            throw new ArgumentException("Source and target pools must be different.");

        var registrations = await _registrationRepo.GetRegistrationsForTransferAsync(
            request.RegistrationIds, request.SourcePoolId, jobId, ct);

        if (registrations.Count == 0)
            throw new ArgumentException("No valid registrations found for transfer.");

        var previews = new List<RosterTransferFeePreviewDto>();
        var isSourceUnassigned = request.SourcePoolId == Guid.Empty;
        var isTargetUnassigned = request.TargetPoolId == Guid.Empty;

        // FLOW 2: Unassigned Adult → Team (staff creation)
        if (isSourceUnassigned && !isTargetUnassigned)
        {
            foreach (var reg in registrations)
            {
                string? warning = null;
                var existing = await _registrationRepo.GetExistingStaffAssignmentAsync(
                    reg.UserId!, request.TargetPoolId, jobId, ct);
                if (existing != null)
                    warning = "Already assigned to this team — transfer will be skipped.";

                previews.Add(new RosterTransferFeePreviewDto
                {
                    RegistrationId = reg.RegistrationId,
                    PlayerName = GetPlayerName(reg),
                    TransferType = "staff-create",
                    CurrentFeeBase = 0,
                    CurrentFeeTotal = 0,
                    NewFeeBase = 0,
                    NewFeeTotal = 0,
                    FeeDelta = 0,
                    Warning = warning
                });
            }
            return previews;
        }

        // FLOW 3: Staff → Unassigned pool (staff removal)
        if (!isSourceUnassigned && isTargetUnassigned)
        {
            foreach (var reg in registrations)
            {
                var roleName = reg.Role?.Name ?? "";
                if (roleName != RoleConstants.Names.StaffName)
                {
                    previews.Add(new RosterTransferFeePreviewDto
                    {
                        RegistrationId = reg.RegistrationId,
                        PlayerName = GetPlayerName(reg),
                        TransferType = "invalid",
                        CurrentFeeBase = reg.FeeBase,
                        CurrentFeeTotal = reg.FeeTotal,
                        NewFeeBase = reg.FeeBase,
                        NewFeeTotal = reg.FeeTotal,
                        FeeDelta = 0,
                        Warning = "Only Staff registrations can be moved to the Unassigned Adults pool."
                    });
                    continue;
                }

                previews.Add(new RosterTransferFeePreviewDto
                {
                    RegistrationId = reg.RegistrationId,
                    PlayerName = GetPlayerName(reg),
                    TransferType = "staff-delete",
                    CurrentFeeBase = reg.FeeBase,
                    CurrentFeeTotal = reg.FeeTotal,
                    NewFeeBase = 0,
                    NewFeeTotal = 0,
                    FeeDelta = -reg.FeeTotal
                });
            }
            return previews;
        }

        // FLOW 1 or 4: Team → Team (player swap or staff move)
        var targetContext = await _teamRepo.GetTeamWithFeeContextAsync(request.TargetPoolId, ct);
        if (targetContext == null)
            throw new ArgumentException("Target team not found.");

        var (targetTeam, targetAgegroup) = targetContext.Value;

        foreach (var reg in registrations)
        {
            var roleName = reg.Role?.Name ?? "";
            if (roleName == RoleConstants.Names.StaffName)
            {
                // FLOW 4: Staff → Different Team (no fee recalc)
                previews.Add(new RosterTransferFeePreviewDto
                {
                    RegistrationId = reg.RegistrationId,
                    PlayerName = GetPlayerName(reg),
                    TransferType = "staff-move",
                    CurrentFeeBase = reg.FeeBase,
                    CurrentFeeTotal = reg.FeeTotal,
                    NewFeeBase = 0,
                    NewFeeTotal = 0,
                    FeeDelta = 0
                });
            }
            else
            {
                // FLOW 1: Player → Team (fee recalc)
                var newFeeBase = CoalescePlayerFee(targetTeam, targetAgegroup);
                var (newProcessing, newTotal) = _feeCalc.ComputeTotals(newFeeBase, reg.FeeDiscount, reg.FeeDonation);

                previews.Add(new RosterTransferFeePreviewDto
                {
                    RegistrationId = reg.RegistrationId,
                    PlayerName = GetPlayerName(reg),
                    TransferType = "player-swap",
                    CurrentFeeBase = reg.FeeBase,
                    CurrentFeeTotal = reg.FeeTotal,
                    NewFeeBase = newFeeBase,
                    NewFeeTotal = newTotal,
                    FeeDelta = newTotal - reg.FeeTotal
                });
            }
        }

        return previews;
    }

    public async Task<RosterTransferResultDto> ExecuteTransferAsync(
        Guid jobId, string adminUserId, RosterTransferRequest request, CancellationToken ct = default)
    {
        if (request.SourcePoolId == request.TargetPoolId)
            throw new ArgumentException("Source and target pools must be different.");

        var registrations = await _registrationRepo.GetRegistrationsForTransferAsync(
            request.RegistrationIds, request.SourcePoolId, jobId, ct);

        if (registrations.Count == 0)
            throw new ArgumentException("No valid registrations found for transfer.");

        var isSourceUnassigned = request.SourcePoolId == Guid.Empty;
        var isTargetUnassigned = request.TargetPoolId == Guid.Empty;
        var now = DateTime.UtcNow;

        int playersTransferred = 0;
        int staffCreated = 0;
        int staffDeleted = 0;
        int feesRecalculated = 0;

        // FLOW 2: Unassigned Adult → Team (staff creation)
        if (isSourceUnassigned && !isTargetUnassigned)
        {
            var targetContext = await _teamRepo.GetTeamWithFeeContextAsync(request.TargetPoolId, ct)
                ?? throw new ArgumentException("Target team not found.");
            var (targetTeam, _) = targetContext;

            // Capacity check
            var currentCount = await _teamRepo.GetPlayerCountAsync(request.TargetPoolId, ct);
            if (targetTeam.MaxCount > 0 && currentCount + registrations.Count > targetTeam.MaxCount)
                throw new InvalidOperationException(
                    $"Target team capacity exceeded. Current: {currentCount}/{targetTeam.MaxCount}, attempting to add {registrations.Count}.");

            foreach (var reg in registrations)
            {
                // Check for duplicate
                var existing = await _registrationRepo.GetExistingStaffAssignmentAsync(
                    reg.UserId!, request.TargetPoolId, jobId, ct);
                if (existing != null) continue;

                // Create new Staff registration
                var staffReg = new Registrations
                {
                    RegistrationId = Guid.NewGuid(),
                    UserId = reg.UserId,
                    FamilyUserId = reg.FamilyUserId,
                    JobId = reg.JobId,
                    RoleId = RoleConstants.Staff,
                    AssignedTeamId = request.TargetPoolId,
                    AssignedAgegroupId = targetTeam.AgegroupId,
                    AssignedDivId = targetTeam.DivId,
                    AssignedLeagueId = targetTeam.LeagueId,
                    BActive = true,
                    FeeBase = 0,
                    FeeProcessing = 0,
                    FeeDiscount = 0,
                    FeeDiscountMp = 0,
                    FeeDonation = 0,
                    FeeLatefee = 0,
                    FeeTotal = 0,
                    OwedTotal = 0,
                    PaidTotal = 0,
                    LebUserId = adminUserId,
                    Modified = now,
                    RegistrationTs = now,
                    BConfirmationSent = false
                };
                _registrationRepo.Add(staffReg);

                // Device sync: mirror source's device links for new Staff reg
                var deviceIds = await _deviceRepo.GetDeviceIdsByRegistrationAsync(reg.RegistrationId, ct);
                foreach (var deviceId in deviceIds)
                {
                    _deviceRepo.AddDeviceTeam(new DeviceTeams
                    {
                        Id = Guid.NewGuid(),
                        DeviceId = deviceId,
                        TeamId = request.TargetPoolId,
                        RegistrationId = staffReg.RegistrationId,
                        Modified = now
                    });
                    _deviceRepo.AddDeviceRegistrationId(new DeviceRegistrationIds
                    {
                        Id = Guid.NewGuid(),
                        DeviceId = deviceId,
                        RegistrationId = staffReg.RegistrationId,
                        Active = true,
                        Modified = now
                    });
                }

                staffCreated++;
            }

            await _registrationRepo.SaveChangesAsync(ct);
            return new RosterTransferResultDto
            {
                PlayersTransferred = 0,
                StaffCreated = staffCreated,
                StaffDeleted = 0,
                FeesRecalculated = 0,
                Message = $"{staffCreated} staff registration(s) created."
            };
        }

        // FLOW 3: Staff → Unassigned pool (staff removal)
        if (!isSourceUnassigned && isTargetUnassigned)
        {
            foreach (var reg in registrations)
            {
                var roleName = reg.Role?.Name ?? "";
                if (roleName != RoleConstants.Names.StaffName) continue;

                // Device cleanup
                var deviceTeams = await _deviceRepo.GetDeviceTeamsByRegistrationAndTeamAsync(
                    reg.RegistrationId, request.SourcePoolId, ct);
                if (deviceTeams.Count > 0)
                    _deviceRepo.RemoveDeviceTeams(deviceTeams);

                var deviceRegIds = await _deviceRepo.GetDeviceRegistrationIdsByRegistrationAsync(
                    reg.RegistrationId, ct);
                if (deviceRegIds.Count > 0)
                    _deviceRepo.RemoveDeviceRegistrationIds(deviceRegIds);

                // Delete the Staff registration
                _registrationRepo.Remove(reg);
                staffDeleted++;
            }

            await _registrationRepo.SaveChangesAsync(ct);
            return new RosterTransferResultDto
            {
                PlayersTransferred = 0,
                StaffCreated = 0,
                StaffDeleted = staffDeleted,
                FeesRecalculated = 0,
                Message = $"{staffDeleted} staff registration(s) removed."
            };
        }

        // FLOW 1 & 4: Team → Team
        {
            var targetContext = await _teamRepo.GetTeamWithFeeContextAsync(request.TargetPoolId, ct)
                ?? throw new ArgumentException("Target team not found.");
            var (targetTeam, targetAgegroup) = targetContext;

            // Capacity check
            var currentCount = await _teamRepo.GetPlayerCountAsync(request.TargetPoolId, ct);
            if (targetTeam.MaxCount > 0 && currentCount + registrations.Count > targetTeam.MaxCount)
                throw new InvalidOperationException(
                    $"Target team capacity exceeded. Current: {currentCount}/{targetTeam.MaxCount}, attempting to add {registrations.Count}.");

            foreach (var reg in registrations)
            {
                var roleName = reg.Role?.Name ?? "";
                var oldTeamId = reg.AssignedTeamId ?? Guid.Empty;

                // Update team assignment
                reg.AssignedTeamId = request.TargetPoolId;
                reg.AssignedAgegroupId = targetTeam.AgegroupId;
                reg.AssignedDivId = targetTeam.DivId;
                reg.AssignedLeagueId = targetTeam.LeagueId;
                reg.Modified = now;
                reg.LebUserId = adminUserId;

                if (roleName == RoleConstants.Names.StaffName)
                {
                    // FLOW 4: Staff → Different Team (no fee recalc)
                    // Entity is already tracked — EF detects property changes automatically
                    playersTransferred++;
                }
                else
                {
                    // FLOW 1: Player → Team (fee recalc)
                    var newFeeBase = CoalescePlayerFee(targetTeam, targetAgegroup);
                    reg.FeeBase = newFeeBase;
                    var (processing, total) = _feeCalc.ComputeTotals(newFeeBase, reg.FeeDiscount, reg.FeeDonation);
                    reg.FeeProcessing = processing;
                    reg.FeeTotal = total;
                    reg.OwedTotal = total - reg.PaidTotal;

                    // Entity is already tracked — EF detects property changes automatically
                    playersTransferred++;
                    feesRecalculated++;
                }

                // Device sync: update DeviceTeams for old team → new team
                if (oldTeamId != Guid.Empty)
                {
                    var deviceTeams = await _deviceRepo.GetDeviceTeamsByRegistrationAndTeamAsync(
                        reg.RegistrationId, oldTeamId, ct);
                    foreach (var dt in deviceTeams)
                    {
                        dt.TeamId = request.TargetPoolId;
                        dt.Modified = now;
                    }
                }
            }

            await _registrationRepo.SaveChangesAsync(ct);

            var parts = new List<string>();
            if (playersTransferred > 0) parts.Add($"{playersTransferred} transferred");
            if (feesRecalculated > 0) parts.Add($"{feesRecalculated} fees recalculated");

            return new RosterTransferResultDto
            {
                PlayersTransferred = playersTransferred,
                StaffCreated = 0,
                StaffDeleted = 0,
                FeesRecalculated = feesRecalculated,
                Message = string.Join(", ", parts) + "."
            };
        }
    }

    public async Task TogglePlayerActiveAsync(
        Guid registrationId, Guid jobId, bool active, string adminUserId, CancellationToken ct = default)
    {
        var reg = await _registrationRepo.GetByIdAsync(registrationId, ct)
            ?? throw new KeyNotFoundException("Registration not found.");

        if (reg.JobId != jobId)
            throw new ArgumentException("Registration does not belong to this job.");

        reg.BActive = active;
        reg.Modified = DateTime.UtcNow;
        reg.LebUserId = adminUserId;
        // Entity is already tracked via FindAsync — EF detects property changes automatically
        await _registrationRepo.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Fee coalescing hierarchy: Team.PerRegistrantFee → Agegroup.PlayerFeeOverride → Agegroup.RosterFee → 0
    /// </summary>
    private static decimal CoalescePlayerFee(TSIC.Domain.Entities.Teams team, TSIC.Domain.Entities.Agegroups agegroup)
    {
        return team.PerRegistrantFee
            ?? agegroup.PlayerFeeOverride
            ?? agegroup.RosterFee
            ?? 0m;
    }

    private static string GetPlayerName(Registrations reg)
    {
        if (reg.User != null)
            return $"{reg.User.LastName ?? ""}, {reg.User.FirstName ?? ""}".Trim().TrimEnd(',').Trim();
        return "Unknown";
    }
}
