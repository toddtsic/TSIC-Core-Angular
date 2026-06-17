using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.VerticalInsure;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.PlayerRegistration.RosterCapacity;

/// <summary>
/// PLAYER ROSTER CAPACITY TESTS
///
/// PreSubmit is the only place registrations are created — it writes a pending hold on the REAL
/// team, then reconciles: a player whose team's CONFIRMED roster is already at max is auto-moved
/// to the $0 WAITLIST twin via SeatReconciliation (the same operation the charge runs as its
/// backstop). A full team never hard-stops the wizard.
///
/// What these tests prove:
///   - A full team proceeds to Payment (no hard stop)
///   - Free events ($0 owed) activate at submit; paid events stay BActive=false until payment
///   - A team change before payment re-prices for the new team; a same-team resubmit does not
///   - A player already rostered on a now-full team keeps their seat (reconcile skips active regs)
///
/// Service under test: PlayerRegistrationService.PreSubmitAsync().
/// All dependencies are mocked. No database involved.
/// </summary>
public class PlayerRosterCapacityTests
{
    // ── Shared test IDs ──────────────────────────────────────────────

    private static readonly Guid TestJobId = Guid.NewGuid();
    private static readonly string TestFamilyUserId = "family-user-test";
    private static readonly string TestPlayerId = "player-test-1";

    // ── Factory: builds service with all mocks ───────────────────────

    private static (
        PlayerRegistrationService svc,
        Mock<IRegistrationRepository> regRepo,
        Mock<ITeamRepository> teamRepo,
        Mock<ITeamPlacementService> placement,
        Mock<IFeeResolutionService> feeService)
        CreateService()
    {
        var logger = new Mock<ILogger<PlayerRegistrationService>>();
        var feeService = new Mock<IFeeResolutionService>();
        var verticalInsure = new Mock<IVerticalInsureService>();
        var teamLookup = new Mock<ITeamLookupService>();
        var validation = new Mock<IPlayerFormValidationService>();
        var regRepo = new Mock<IRegistrationRepository>();
        var teamRepo = new Mock<ITeamRepository>();
        var jobRepo = new Mock<IJobRepository>();
        var placement = new Mock<ITeamPlacementService>();

        // Default: validation returns no errors
        validation
            .Setup(v => v.ValidatePlayerFormValues(It.IsAny<string?>(), It.IsAny<List<PreSubmitTeamSelectionDto>>()))
            .Returns(new List<PreSubmitValidationErrorDto>());

        // Default: insurance returns unavailable
        verticalInsure
            .Setup(v => v.BuildOfferAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(new PreSubmitInsuranceDto { Available = false });

        // Default: job metadata returns PP mode (standard player registration)
        jobRepo
            .Setup(j => j.GetPreSubmitMetadataAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPreSubmitMetadata
            {
                PlayerProfileMetadataJson = null,
                JsonOptions = null,
                CoreRegformPlayer = "PP10",
            });

        // Default: no existing registrations for any player in this family
        regRepo
            .Setup(r => r.GetFamilyRegistrationsForPlayersAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations>());
        regRepo
            .Setup(r => r.GetFamilyRegistrationsForPlayersTrackedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations>());

        // Default: the post-save reconcile fetch returns no family regs (reconcile is a no-op
        // unless a test sets this up), and the raw-team snapshot is empty.
        regRepo
            .Setup(r => r.GetByJobAndFamilyWithUsersAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations>());
        teamLookup
            .Setup(t => t.GetAvailableTeamsForJobAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new List<AvailableTeamDto>());

        // Default: SaveChangesAsync succeeds
        regRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Default: the selected team has a configured fee. This is the precondition for reaching
        // the create/update logic — the fail-loud guard short-circuits with a "Fee not set" failed
        // result when no fee is configured.
        feeService
            .Setup(f => f.ResolveFeeAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { FeeConfigured = true, Deposit = 0m, BalanceDue = 100m });

        var svc = new PlayerRegistrationService(
            logger.Object,
            feeService.Object,
            verticalInsure.Object,
            teamLookup.Object,
            validation.Object,
            regRepo.Object,
            teamRepo.Object,
            jobRepo.Object,
            placement.Object,
            new Mock<IMedFormService>().Object);

        return (svc, regRepo, teamRepo, placement, feeService);
    }

    // ── Helper: configure a team with a specific roster count ─────────

    private static Teams SetupTeamWithRoster(
        Mock<ITeamRepository> teamRepo,
        Mock<IRegistrationRepository> regRepo,
        int maxCount,
        int currentRosterCount)
    {
        var team = RegistrationDataBuilder.BuildTeam(TestJobId, Guid.NewGuid(), maxCount);

        teamRepo
            .Setup(t => t.GetTeamsForJobAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams> { team });

        regRepo
            .Setup(r => r.GetRosterCountsByTeamAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { { team.TeamId, currentRosterCount } });

        return team;
    }

    /// <summary>Configure the fee mock so applying fees stamps the given base and recomputes totals
    /// (OwedTotal), mirroring the real FeeResolutionService — the mock otherwise leaves fees at 0.</summary>
    private static void SetupFee(Mock<IFeeResolutionService> feeService, decimal balanceDue)
    {
        feeService
            .Setup(f => f.ResolveFeeAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { FeeConfigured = true, Deposit = 0m, BalanceDue = balanceDue });

        feeService
            .Setup(f => f.ApplyNewRegistrationFeesAsync(
                It.IsAny<Registrations>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()))
            .Callback<Registrations, Guid, Guid, Guid, FeeApplicationContext, CancellationToken>(
                (reg, _, _, _, _, _) => { reg.FeeBase = balanceDue; reg.RecalcTotals(); })
            .Returns(Task.CompletedTask);
    }

    private static PreSubmitPlayerRegistrationRequestDto MakePreSubmitRequest(Guid teamId) => new()
    {
        JobPath = "test-job",
        TeamSelections = new List<PreSubmitTeamSelectionDto>
        {
            new() { PlayerId = TestPlayerId, TeamId = teamId }
        }
    };

    // ── Tests ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "PreSubmit: team full → NextTab = 'Payment' (proceed; payment does the waitlisting)")]
    public async Task PreSubmit_TeamFull_NextTabIsPayment()
    {
        // A full team no longer bounces the user back to team selection. They proceed to Payment,
        // where the cart-split moves a seat-gone player to the $0 waitlist twin (not charged).
        var (svc, regRepo, teamRepo, _, feeService) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 10);
        SetupFee(feeService, balanceDue: 100m);

        // Act
        var result = await svc.PreSubmitAsync(TestJobId, TestFamilyUserId, MakePreSubmitRequest(team.TeamId), TestFamilyUserId);

        // Assert — proceed to payment, which performs the waitlisting
        result.NextTab.Should().Be("Payment",
            "a full team now proceeds to payment, which performs the waitlisting");
        result.HasFullTeams.Should().BeFalse("full no longer hard-stops the wizard");
    }

    // ── Free-event activation (zero-balance) ─────────────────────────────
    //
    // A configured $0-fee event leaves nothing owed, so there is no payment to ride.
    // Unless the final submit flips BActive, the reg stays inactive forever — off rosters,
    // missing from the confirmation (the same symptom a 100% discount code produced).
    // Legacy: "free events start life active, otherwise start inactive and convert upon payment."

    [Fact(DisplayName = "PreSubmit: free event ($0 fee) → new reg lands BActive=true (owes nothing → active)")]
    public async Task PreSubmit_FreeEvent_ActivatesNewRegistration()
    {
        var (svc, regRepo, teamRepo, _, feeService) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 0);
        SetupFee(feeService, balanceDue: 0m);

        Registrations? captured = null;
        regRepo.Setup(r => r.Add(It.IsAny<Registrations>())).Callback<Registrations>(reg => captured = reg);
        // Activation now runs AFTER reconcile, over the post-save family fetch — so that fetch must
        // surface the just-created reg for ActivateIfFree to flip it. (Lazy: captured is set during
        // PreSubmit's Add, before this fetch runs.)
        regRepo
            .Setup(r => r.GetByJobAndFamilyWithUsersAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => captured is null ? new List<Registrations>() : new List<Registrations> { captured });

        await svc.PreSubmitAsync(TestJobId, TestFamilyUserId, MakePreSubmitRequest(team.TeamId), TestFamilyUserId);

        captured.Should().NotBeNull("a registration should have been created");
        captured!.OwedTotal.Should().Be(0m);
        captured.BActive.Should().BeTrue("a free event owes nothing, so it activates at final submit");
    }

    [Fact(DisplayName = "PreSubmit: paid event → new reg stays BActive=false (activates only on payment)")]
    public async Task PreSubmit_PaidEvent_LeavesNewRegistrationInactive()
    {
        var (svc, regRepo, teamRepo, _, feeService) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 0);
        SetupFee(feeService, balanceDue: 100m);

        Registrations? captured = null;
        regRepo.Setup(r => r.Add(It.IsAny<Registrations>())).Callback<Registrations>(reg => captured = reg);

        await svc.PreSubmitAsync(TestJobId, TestFamilyUserId, MakePreSubmitRequest(team.TeamId), TestFamilyUserId);

        captured.Should().NotBeNull();
        captured!.OwedTotal.Should().Be(100m);
        captured.BActive.Should().BeFalse("a paid event still activates only when payment clears");
    }

    // ── Team change before payment re-prices the registration ────────────
    //
    // Parent reaches Payment, goes BACK to team selection, picks a DIFFERENT team, advances.
    // The reg already has the old team's fee stamped. ApplyInitialFeesAsync no-ops once
    // FeeBase>0, so without an explicit re-stamp the moved reg would keep the OLD team's fee
    // and the payment tab would show the prior team's numbers. The no-payment team-change path
    // must re-stamp the fee for the NEW team.

    [Fact(DisplayName = "PreSubmit: team changed before payment → fee re-stamped for the NEW team")]
    public async Task PreSubmit_TeamChangedBeforePayment_RestampsFeeForNewTeam()
    {
        var (svc, regRepo, teamRepo, _, feeService) = CreateService();

        // Existing pending reg on team A, already priced at 100 (team A's fee).
        var teamAId = Guid.NewGuid();
        var regA = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = TestJobId,
            UserId = TestPlayerId,
            FamilyUserId = TestFamilyUserId,
            AssignedTeamId = teamAId,
            AssignedAgegroupId = Guid.NewGuid(),
            FeeBase = 100m,
            FeeTotal = 100m,
            OwedTotal = 100m,
            PaidTotal = 0m,
            BActive = false,
            Modified = DateTime.Now,
        };

        // Parent re-picks team B, priced at 250.
        var teamB = RegistrationDataBuilder.BuildTeam(TestJobId, Guid.NewGuid(), maxCount: 10);
        teamRepo
            .Setup(t => t.GetTeamsForJobAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams> { teamB });
        regRepo
            .Setup(r => r.GetRosterCountsByTeamAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { { teamB.TeamId, 0 } });
        regRepo
            .Setup(r => r.GetFamilyRegistrationsForPlayersTrackedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { regA });
        feeService
            .Setup(f => f.ResolveFeeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { FeeConfigured = true, Deposit = 0m, BalanceDue = 250m });
        feeService
            .Setup(f => f.ApplyNewRegistrationFeesAsync(
                It.IsAny<Registrations>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()))
            .Callback<Registrations, Guid, Guid, Guid, FeeApplicationContext, CancellationToken>(
                (reg, _, _, _, _, _) => { reg.FeeBase = 250m; reg.RecalcTotals(); })
            .Returns(Task.CompletedTask);

        await svc.PreSubmitAsync(TestJobId, TestFamilyUserId, MakePreSubmitRequest(teamB.TeamId), TestFamilyUserId);

        regA.AssignedTeamId.Should().Be(teamB.TeamId, "the reg should move to the newly selected team");
        regA.FeeBase.Should().Be(250m, "the fee must be re-stamped for the new team, not left at the old team's price");
        regA.OwedTotal.Should().Be(250m, "owed must reflect the new team's price");
        feeService.Verify(f => f.ApplyNewRegistrationFeesAsync(
            regA, TestJobId, teamB.AgegroupId, teamB.TeamId, It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()),
            Times.Once, "a team change before payment forces a fresh fee stamp for the new team");
    }

    // ── Admin roster-swap onto a full team must survive the parent's review ──────
    //
    // A player is waitlisted, then an admin roster-swaps them onto a real team that is
    // (or becomes) at capacity. The player's own active reg is counted in the roster
    // total, so the team reads "full". When the parent returns to review/edit, the
    // reconcile must NOT bounce the player back to the waitlist mirror — they already
    // hold a legitimate seat on that team (SeatReconciliation skips BActive=true regs).

    [Fact(DisplayName = "PreSubmit: player already rostered on a now-full team is NOT bounced to waitlist")]
    public async Task PreSubmit_PlayerAlreadyOnFullTeam_KeepsTheirSeat()
    {
        var (svc, regRepo, teamRepo, placement, feeService) = CreateService();

        // Team is at capacity (10/10) — and the registrant under review is one of those 10.
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 10);
        SetupFee(feeService, balanceDue: 100m);

        // The player already holds an active reg on this exact team (admin roster-swapped them in).
        var existing = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = TestJobId,
            UserId = TestPlayerId,
            FamilyUserId = TestFamilyUserId,
            AssignedTeamId = team.TeamId,
            AssignedAgegroupId = team.AgegroupId,
            FeeBase = 100m,
            FeeTotal = 100m,
            OwedTotal = 100m,
            PaidTotal = 0m,
            BActive = true,
            Modified = DateTime.Now,
        };
        regRepo
            .Setup(r => r.GetFamilyRegistrationsForPlayersTrackedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { existing });
        // The reconcile fetch sees the same active reg — and must skip it (BActive=true owns its seat).
        regRepo
            .Setup(r => r.GetByJobAndFamilyWithUsersAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { existing });

        // Act
        var result = await svc.PreSubmitAsync(TestJobId, TestFamilyUserId, MakePreSubmitRequest(team.TeamId), TestFamilyUserId);

        // Assert — the reg stays on the real team and no waitlist redirect happened.
        result.TeamResults.Should().HaveCount(1);
        result.TeamResults[0].IsFull.Should().BeFalse();
        result.TeamResults[0].TeamId.Should().Be(team.TeamId, "the player must remain on their assigned team");
        existing.AssignedTeamId.Should().Be(team.TeamId, "the existing active reg must not be moved to the waitlist mirror");

        placement.Verify(p => p.ResolveRosterPlacementAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "an already-rostered active player must skip the full-team waitlist redirect entirely");
        regRepo.Verify(r => r.Add(It.IsAny<Registrations>()), Times.Never,
            "no new registration — the existing one is updated in place");
    }

    [Fact(DisplayName = "PreSubmit: same team re-submitted → fee NOT recomputed (no spurious re-stamp)")]
    public async Task PreSubmit_SameTeamResubmitted_DoesNotRestampFee()
    {
        var (svc, regRepo, teamRepo, _, feeService) = CreateService();

        var teamA = RegistrationDataBuilder.BuildTeam(TestJobId, Guid.NewGuid(), maxCount: 10);
        var regA = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = TestJobId,
            UserId = TestPlayerId,
            FamilyUserId = TestFamilyUserId,
            AssignedTeamId = teamA.TeamId,
            AssignedAgegroupId = teamA.AgegroupId,
            FeeBase = 100m,
            FeeTotal = 100m,
            OwedTotal = 100m,
            PaidTotal = 0m,
            BActive = false,
            Modified = DateTime.Now,
        };

        teamRepo
            .Setup(t => t.GetTeamsForJobAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams> { teamA });
        regRepo
            .Setup(r => r.GetRosterCountsByTeamAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { { teamA.TeamId, 0 } });
        regRepo
            .Setup(r => r.GetFamilyRegistrationsForPlayersTrackedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { regA });
        feeService
            .Setup(f => f.ResolveFeeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { FeeConfigured = true, Deposit = 0m, BalanceDue = 100m });

        await svc.PreSubmitAsync(TestJobId, TestFamilyUserId, MakePreSubmitRequest(teamA.TeamId), TestFamilyUserId);

        regA.FeeBase.Should().Be(100m, "no team change → existing fee is preserved");
        feeService.Verify(f => f.ApplyNewRegistrationFeesAsync(
            It.IsAny<Registrations>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()),
            Times.Never, "re-submitting the same team must not re-stamp the fee");
    }
}
