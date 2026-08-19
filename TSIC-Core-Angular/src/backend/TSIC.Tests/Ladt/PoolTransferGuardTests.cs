using FluentAssertions;
using Moq;
using TSIC.API.Services.Admin;
using TSIC.Contracts.Dtos.PoolAssignment;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Ladt;

/// <summary>
/// Pre-flight guard on <see cref="PoolAssignmentService.ExecuteTransferAsync"/>.
///
/// A symmetrical swap validates that source and target ID counts MATCH, but not that every
/// target id actually resolves to a team in this job. An id that resolves to nothing used to
/// fault the move loop on the DivRank pairing lookup — partway through the operation, after
/// work had begun. The guard moves that failure up next to the other pre-flight validations,
/// so the transfer either cannot start or can finish.
///
/// This matters beyond tidiness: the team-scoped fee repoint is staged just below the guard.
/// It is staged TRACKED and commits with the team move in one SaveChanges (see
/// TeamFeeScopeInvariantTests), so a fault cannot half-commit — but the transfer should still
/// refuse an impossible request before touching anything at all.
/// </summary>
public class PoolTransferGuardTests
{
    [Fact(DisplayName = "Symmetrical swap: a target team id resolving to nothing is rejected before any staging")]
    public async Task ExecuteTransfer_UnresolvedTargetTeam_ThrowsBeforeStagingAnything()
    {
        var jobId = Guid.NewGuid();
        var sourceDivId = Guid.NewGuid();
        var targetDivId = Guid.NewGuid();
        var sourceAgId = Guid.NewGuid();
        var targetAgId = Guid.NewGuid();
        var sourceTeamId = Guid.NewGuid();
        var missingTargetTeamId = Guid.NewGuid();

        var divRepo = new Mock<IDivisionRepository>();
        divRepo.Setup(d => d.GetByIdReadOnlyAsync(sourceDivId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Divisions { DivId = sourceDivId, AgegroupId = sourceAgId });
        divRepo.Setup(d => d.GetByIdReadOnlyAsync(targetDivId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Divisions { DivId = targetDivId, AgegroupId = targetAgId });

        var teamRepo = new Mock<ITeamRepository>();
        teamRepo.Setup(t => t.GetTeamsForPoolTransferAsync(
                It.Is<List<Guid>>(l => l.Contains(sourceTeamId)), jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams>
            {
                new() { TeamId = sourceTeamId, JobId = jobId, AgegroupId = sourceAgId, DivId = sourceDivId }
            });
        // Stale, deleted, or cross-job id — resolves to nothing.
        teamRepo.Setup(t => t.GetTeamsForPoolTransferAsync(
                It.Is<List<Guid>>(l => l.Contains(missingTargetTeamId)), jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams>());
        teamRepo.Setup(t => t.GetScheduledTeamIdsAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        var agRepo = new Mock<IAgeGroupRepository>();
        agRepo.Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new Agegroups { AgegroupId = id, AgegroupName = "U12" });

        var feeSvc = new Mock<IFeeResolutionService>();

        var svc = new PoolAssignmentService(
            teamRepo.Object,
            divRepo.Object,
            new Mock<IScheduleRepository>().Object,
            new Mock<IRegistrationRepository>().Object,
            agRepo.Object,
            feeSvc.Object,
            new Mock<IPaymentStateService>().Object);

        var act = async () => await svc.ExecuteTransferAsync(jobId, "admin-user", new PoolTransferRequest
        {
            SourceDivId = sourceDivId,
            TargetDivId = targetDivId,
            SourceTeamIds = new List<Guid> { sourceTeamId },
            TargetTeamIds = new List<Guid> { missingTargetTeamId },
            IsSymmetricalSwap = true
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*target teams were not found*");

        feeSvc.Verify(f => f.RepointTeamScopedFeesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "the guard sits ABOVE the fee repoint — nothing may be staged for a move that cannot complete");
        teamRepo.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never, "nothing is written");
    }
}
