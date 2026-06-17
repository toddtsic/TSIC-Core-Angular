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
/// The reserve/PreSubmit step does NOT gate roster capacity — it writes a pending hold on the
/// REAL team and proceeds. The roster-max guarantee is enforced at PAYMENT (the cart-split moves
/// a seat-gone player to the $0 waitlist twin, never charged/activated).
///
/// What these tests prove:
///   - A team with room accepts the registration
///   - A full team does NOT hard-stop — the player is held on the real team and proceeds
///   - An already-rostered player keeps their seat (no waitlist redirect)
///   - New reserve/PreSubmit regs stay BActive=false until payment (free events activate at submit)
///
/// Service under test: PlayerRegistrationService.ReserveTeamsAsync() / PreSubmitAsync()
/// All 9 dependencies are mocked. No database involved.
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

        // Default: SaveChangesAsync succeeds
        regRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Default: the selected team has a configured fee. This is the precondition for
        // reaching the roster-capacity logic — the fail-loud guard short-circuits with a
        // "Fee not set" failed result before any reserve when no fee is configured.
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

    // ── Helper: build a ReserveTeams request ─────────────────────────

    private static ReserveTeamsRequestDto MakeReserveRequest(Guid teamId)
    {
        return new ReserveTeamsRequestDto
        {
            JobPath = "test-job",
            TeamSelections = new List<ReserveTeamSelectionDto>
            {
                new() { PlayerId = TestPlayerId, TeamId = teamId }
            }
        };
    }

    // ── Tests ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "Reserve: team has room (9/10) → registration created, IsFull = false")]
    public async Task Reserve_TeamHasRoom_CreatesRegistration()
    {
        // Arrange — team allows 10 players, currently has 9
        var (svc, regRepo, teamRepo, _, _) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 9);

        // Act
        var result = await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeReserveRequest(team.TeamId), TestFamilyUserId);

        // Assert — registration was created, team is not full
        result.TeamResults.Should().HaveCount(1);
        result.TeamResults[0].IsFull.Should().BeFalse("team has room for 1 more player");
        result.HasFullTeams.Should().BeFalse();

        // Verify a registration entity was added
        regRepo.Verify(r => r.Add(It.Is<Registrations>(reg =>
            reg.AssignedTeamId == team.TeamId &&
            reg.UserId == TestPlayerId)),
            Times.Once, "should create a pending registration on the selected team");
    }

    [Fact(DisplayName = "Reserve: team full → held on the REAL team (placement deferred to payment)")]
    public async Task Reserve_TeamFull_HoldsOnRealTeam()
    {
        // Arrange — team is at capacity. Waitlists are mandatory, so a full team is no longer a hard
        // stop; but the selection step must NOT shove the player onto the $0 waitlist twin (doing so
        // used to let ActivateIfFree mark an unpaid reg active just for reaching forms). The pending
        // hold lands on the REAL team; the payment cart-split is the sole place a seat-gone player is
        // moved to the twin.
        var (svc, regRepo, teamRepo, placement, feeService) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 10);
        SetupFee(feeService, balanceDue: 100m);

        Registrations? captured = null;
        regRepo.Setup(r => r.Add(It.IsAny<Registrations>())).Callback<Registrations>(reg => captured = reg);

        // Act
        var result = await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeReserveRequest(team.TeamId), TestFamilyUserId);

        // Assert — registered on the REAL team; a full team no longer hard-stops the wizard
        result.TeamResults.Should().HaveCount(1);
        result.TeamResults[0].IsFull.Should().BeFalse("full no longer hard-stops; the player proceeds");

        captured.Should().NotBeNull("a pending hold is created on the REAL team");
        captured!.AssignedTeamId.Should().Be(team.TeamId, "the hold stays on the real team — never the twin — until payment");
        captured.BActive.Should().BeFalse("reserve never activates");

        // The selection step must NOT resolve/redirect placement — that is the payment cart-split's job.
        placement.Verify(p => p.ResolveRosterPlacementAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "team placement is deferred to payment");
    }

    [Fact(DisplayName = "Reserve: MaxCount = 0 (unlimited) → never full, placement never called")]
    public async Task Reserve_MaxCountZero_NeverFull()
    {
        // Arrange — team has MaxCount = 0 (unlimited), even with 999 players
        var (svc, regRepo, teamRepo, placement, _) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 0, currentRosterCount: 999);

        // Act
        var result = await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeReserveRequest(team.TeamId), TestFamilyUserId);

        // Assert — unlimited teams are never full
        result.TeamResults.Should().HaveCount(1);
        result.TeamResults[0].IsFull.Should().BeFalse("MaxCount = 0 means unlimited");
        result.HasFullTeams.Should().BeFalse();

        // Placement service should never be called (capacity check is skipped entirely)
        placement.Verify(p => p.ResolveRosterPlacementAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "capacity check should be skipped for unlimited teams");
    }

    [Fact(DisplayName = "Reserve: new registrations land BActive=false (lock-to-check invariant)")]
    public async Task Reserve_NewRegistrations_LandInactive()
    {
        // The pay-by-check submit endpoint is the ONLY surface that flips BActive=true
        // at intake. All other create paths — including CC, ARB, and eCheck — must
        // leave BActive=false until payment clears. This pins that invariant against
        // future drift: if anyone ever sets BActive=true inside ReserveTeamsAsync /
        // CreateNewRegistrationAsync, this test fails immediately.
        var (svc, regRepo, teamRepo, _, _) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 0);

        Registrations? captured = null;
        regRepo
            .Setup(r => r.Add(It.IsAny<Registrations>()))
            .Callback<Registrations>(reg => captured = reg);

        var result = await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeReserveRequest(team.TeamId), TestFamilyUserId);

        result.TeamResults[0].IsFull.Should().BeFalse();
        captured.Should().NotBeNull("a registration should have been added");
        captured!.BActive.Should().Be(false,
            "new registrations created via ReserveTeams must remain Inactive until payment clears " +
            "(only the pay-by-check submit endpoint may flip BActive=true at intake)");
        captured.PaymentMethodChosen.Should().BeNull(
            "no payment method is chosen until the parent reaches the payment step");
    }

    [Fact(DisplayName = "Reserve: family submits 3 to a team with 1 spot → all 3 held on real team (payment enforces max)")]
    public async Task Reserve_FamilyOverflow_AllHeldOnRealTeam()
    {
        // Arrange — team has 1 spot left (MaxCount=10, currentRosterCount=9). A single family submits
        // three players to that same team. Reserve does NOT gate capacity — it holds every sibling on
        // the REAL team. The roster-max guarantee is enforced at PAYMENT by the cart-split, not here.
        var (svc, regRepo, teamRepo, placement, feeService) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 9);
        SetupFee(feeService, balanceDue: 100m);

        var request = new ReserveTeamsRequestDto
        {
            JobPath = "test-job",
            TeamSelections = new List<ReserveTeamSelectionDto>
            {
                new() { PlayerId = "player-1", TeamId = team.TeamId },
                new() { PlayerId = "player-2", TeamId = team.TeamId },
                new() { PlayerId = "player-3", TeamId = team.TeamId },
            }
        };

        // Act
        var result = await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, request, TestFamilyUserId);

        // Assert — all three held on the real team; full no longer hard-stops at reserve.
        result.TeamResults.Should().HaveCount(3);
        result.TeamResults.Should().OnlyContain(r => !r.IsFull, "full no longer hard-stops at reserve");

        regRepo.Verify(r => r.Add(It.IsAny<Registrations>()), Times.Exactly(3),
            "every sibling is held on the real team; payment — not reserve — enforces the roster max");

        placement.Verify(p => p.ResolveRosterPlacementAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "placement is deferred to payment");
    }

    [Fact(DisplayName = "PreSubmit: team full → NextTab = 'Payment' (proceed; payment does the waitlisting)")]
    public async Task PreSubmit_TeamFull_NextTabIsPayment()
    {
        // A full team no longer bounces the user back to team selection. They proceed to Payment,
        // where the cart-split moves a seat-gone player to the $0 waitlist twin (not charged).
        var (svc, regRepo, teamRepo, _, feeService) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 10);
        SetupFee(feeService, balanceDue: 100m);

        var request = new PreSubmitPlayerRegistrationRequestDto
        {
            JobPath = "test-job",
            TeamSelections = new List<PreSubmitTeamSelectionDto>
            {
                new() { PlayerId = TestPlayerId, TeamId = team.TeamId }
            }
        };

        // Act
        var result = await svc.PreSubmitAsync(TestJobId, TestFamilyUserId, request, TestFamilyUserId);

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

    [Fact(DisplayName = "PreSubmit: free event ($0 fee) → new reg lands BActive=true (owes nothing → active)")]
    public async Task PreSubmit_FreeEvent_ActivatesNewRegistration()
    {
        var (svc, regRepo, teamRepo, _, feeService) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 0);
        SetupFee(feeService, balanceDue: 0m);

        Registrations? captured = null;
        regRepo.Setup(r => r.Add(It.IsAny<Registrations>())).Callback<Registrations>(reg => captured = reg);

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

    [Fact(DisplayName = "Reserve: free event → BActive=false (spot-hold before forms must stay inactive)")]
    public async Task Reserve_FreeEvent_StaysInactiveUntilFinalSubmit()
    {
        var (svc, regRepo, teamRepo, _, feeService) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 0);
        SetupFee(feeService, balanceDue: 0m);

        Registrations? captured = null;
        regRepo.Setup(r => r.Add(It.IsAny<Registrations>())).Callback<Registrations>(reg => captured = reg);

        await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeReserveRequest(team.TeamId), TestFamilyUserId);

        captured.Should().NotBeNull();
        // Reserve holds the roster spot BEFORE forms/waivers — a free reg must not go active yet,
        // exactly like a paid reg. Activation waits for the final (post-forms) submit.
        captured!.BActive.Should().BeFalse("reserve never activates; only the final submit does");
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
    // capacity re-check must NOT bounce the player back to the waitlist mirror — they
    // already hold a legitimate seat on that team. Only a genuinely NEW placement
    // overflows. (Bug-before-fix: isFull ignored the player's existing reg and the
    // PreSubmit silently swapped them onto the WAITLIST twin.)

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

        // Act
        var result = await svc.PreSubmitAsync(TestJobId, TestFamilyUserId, MakePreSubmitRequest(team.TeamId), TestFamilyUserId);

        // Assert — the reg stays on the real team and no waitlist redirect happened.
        result.TeamResults.Should().HaveCount(1);
        result.TeamResults[0].IsFull.Should().BeFalse();
        result.TeamResults[0].TeamId.Should().Be(team.TeamId, "the player must remain on their assigned team");
        existing.AssignedTeamId.Should().Be(team.TeamId, "the existing reg must not be moved to the waitlist mirror");

        placement.Verify(p => p.ResolveRosterPlacementAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "an already-rostered player must skip the full-team waitlist redirect entirely");
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
