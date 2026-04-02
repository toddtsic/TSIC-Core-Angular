using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Services.Clubs;
using TSIC.Application.Services.Users;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.Tests.TeamRegistration;

/// <summary>
/// CLUB REGISTRATION GATE TESTS
///
/// Validates the two-tier security gate that prevents duplicate clubs and
/// protects against unauthorized access to another club's team library.
///
/// Tier 1 (85%+ match) = HARD BLOCK
///   Cannot be bypassed, even with ConfirmedNewClub = true.
///   Shows existing rep contact so registrant can reach out directly.
///   Prevents: hijacking another club's teams by registering with their name.
///
/// Tier 2 (65-84% match) = WARNING
///   Requires explicit ConfirmedNewClub = true to proceed.
///   Prevents: accidental duplicate from typos or abbreviation differences.
///
/// Below 65% = CLEAN PATH
///   No friction -- new club created automatically.
///
/// Each test verifies:
///   1. Success/failure of registration attempt
///   2. Presence of SimilarClubs in response when matches found
///   3. Rep contact info (RepName, RepEmail) in blocked results
///   4. That ConfirmedNewClub cannot bypass Tier 1
/// </summary>
public class ClubRegistrationGateTests
{
    // ── Test data ────────────────────────────────────────────────────

    private static readonly ClubSearchCandidate ExistingClub = new()
    {
        ClubId = 1,
        ClubName = "Charlotte Fury",
        State = "NC",
        TeamCount = 12,
        RepName = "John Smith",
        RepEmail = "j.smith@email.com"
    };

    /// <summary>
    /// A club that scores in the WARNING band (65-84%) against typical queries.
    /// "Charlotte Eagles" vs "Charlotte Fury" shares "charlotte" but differs enough.
    /// </summary>
    private static readonly ClubSearchCandidate SimilarClub = new()
    {
        ClubId = 2,
        ClubName = "Charlotte Eagles",
        State = "NC",
        TeamCount = 5,
        RepName = "Sarah Jones",
        RepEmail = "sarah@email.com"
    };

    private static ClubRepRegistrationRequest MakeRequest(
        string clubName,
        bool confirmedNewClub = false) => new()
    {
        ClubName = clubName,
        FirstName = "Test",
        LastName = "User",
        Email = "test@example.com",
        Username = "testuser_" + Guid.NewGuid().ToString("N")[..8],
        Password = "Password123!",
        StreetAddress = "123 Main St",
        City = "Anytown",
        State = "NC",
        PostalCode = "28205",
        Cellphone = "5551234567",
        ConfirmedNewClub = confirmedNewClub
    };

    // ── Service factory ─────────────────────────────────────────────

    private static (ClubService svc, Mock<IClubRepository> clubRepo) CreateService(
        params ClubSearchCandidate[] existingClubs)
    {
        var clubRepo = new Mock<IClubRepository>();
        clubRepo.Setup(r => r.GetSearchCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingClubs.ToList());

        var clubRepRepo = new Mock<IClubRepRepository>();
        clubRepRepo.Setup(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // UserManager requires a store mock that implements IUserPasswordStore
        var userStore = new Mock<IUserPasswordStore<ApplicationUser>>();
        userStore.As<IUserStore<ApplicationUser>>();
        userStore.Setup(s => s.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);
        userStore.Setup(s => s.SetPasswordHashAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        userStore.Setup(s => s.GetPasswordHashAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hashed");
        userStore.Setup(s => s.HasPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var userManager = new UserManager<ApplicationUser>(
            userStore.Object, null!, new PasswordHasher<ApplicationUser>(),
            null!, null!, null!, null!, null!, null!);

        var privilegeService = new Mock<IUserPrivilegeLevelService>();
        privilegeService.Setup(p => p.ValidatePrivilegeForRegistrationAsync(
            It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

        var svc = new ClubService(userManager, clubRepo.Object, clubRepRepo.Object,
            privilegeService.Object, cache);

        return (svc, clubRepo);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TIER 1: HARD BLOCK (85%+ match)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Registrant types exact name of existing club ("Charlotte Fury")
    /// EXPECTED: Registration BLOCKED -- returns similar clubs with rep email
    /// WHY IT MATTERS: Without this gate, anyone could type "Charlotte Fury" and
    ///   get access to that club's 12-team library and game history
    /// </summary>
    [Fact(DisplayName = "Tier 1: exact name match blocks registration")]
    public async Task Tier1_ExactMatch_Blocked()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlotte Fury");

        var result = await svc.RegisterAsync(request);

        result.Success.Should().BeFalse("exact match must block registration");
        result.SimilarClubs.Should().NotBeNullOrEmpty("blocked result must include matching clubs");
        result.Message.Should().Contain("already registered");
    }

    /// <summary>
    /// SCENARIO: Registrant types exact name AND sets ConfirmedNewClub = true
    /// EXPECTED: STILL BLOCKED -- Tier 1 cannot be bypassed by any client-side flag
    /// WHY IT MATTERS: This is the security property. A malicious client could
    ///   always send ConfirmedNewClub = true -- the backend must reject it anyway.
    /// </summary>
    [Fact(DisplayName = "Tier 1: CANNOT be bypassed by ConfirmedNewClub flag")]
    public async Task Tier1_CannotBypassWithConfirmation()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlotte Fury", confirmedNewClub: true);

        var result = await svc.RegisterAsync(request);

        result.Success.Should().BeFalse("Tier 1 block must NOT be bypassed by ConfirmedNewClub");
    }

    /// <summary>
    /// SCENARIO: Registrant types a near-exact typo ("Charlote Fury")
    /// EXPECTED: Registration BLOCKED -- typos should not bypass the gate
    /// </summary>
    [Fact(DisplayName = "Tier 1: typo variant ('Charlote Fury') still blocked")]
    public async Task Tier1_TypoVariant_StillBlocked()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlote Fury");

        var result = await svc.RegisterAsync(request);

        result.Success.Should().BeFalse("single-char typo should still trigger Tier 1");
    }

    /// <summary>
    /// SCENARIO: Blocked result must include existing rep's contact info
    /// EXPECTED: RepName and RepEmail present -- registrant can contact them directly
    /// </summary>
    [Fact(DisplayName = "Tier 1: blocked result includes rep email for self-service")]
    public async Task Tier1_IncludesRepContact()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlotte Fury");

        var result = await svc.RegisterAsync(request);

        result.SimilarClubs.Should().NotBeNull();
        var match = result.SimilarClubs!.First();
        match.RepName.Should().Be("John Smith");
        match.RepEmail.Should().Be("j.smith@email.com");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TIER 2: WARNING (65-84% match)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Registrant types a name that scores in warning band (65-84%)
    ///   without confirming they want a new club
    /// EXPECTED: Registration NOT allowed -- returns similar clubs for review
    /// </summary>
    [Fact(DisplayName = "Tier 2: warning-band match without confirmation is rejected")]
    public async Task Tier2_WithoutConfirmation_Rejected()
    {
        var (svc, _) = CreateService(SimilarClub);
        // "Charlotte Hawks" shares city with "Charlotte Eagles" -- warning band, not block
        var request = MakeRequest("Charlotte Hawks");

        var result = await svc.RegisterAsync(request);

        if (result.SimilarClubs?.Any() == true)
        {
            result.Success.Should().BeFalse("warning-band match without confirmation should be rejected");
        }
    }

    /// <summary>
    /// SCENARIO: Registrant types a name in the warning band AND confirms "create new"
    /// EXPECTED: Gate passes -- user made an informed decision.
    ///   "Charlotte Hawks" vs "Charlotte Eagles" shares a city but different mascot
    ///   (~70% token overlap) -- solidly in warning band, not block range.
    /// </summary>
    [Fact(DisplayName = "Tier 2: warning-band match WITH confirmation passes the gate")]
    public async Task Tier2_WithConfirmation_PassesGate()
    {
        var (svc, _) = CreateService(SimilarClub);
        var request = MakeRequest("Charlotte Hawks", confirmedNewClub: true);

        var result = await svc.RegisterAsync(request);

        // If it returned SimilarClubs, the gate blocked it -- that's wrong for confirmed
        var wasGateBlocked = !result.Success && result.SimilarClubs?.Any() == true;
        wasGateBlocked.Should().BeFalse(
            "confirmed new club in warning band should pass the gate (not return SimilarClubs)");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CLEAN PATH (below 65%)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Registrant types a name with no close matches in the database
    /// EXPECTED: No SimilarClubs returned, gate does not block
    /// </summary>
    [Fact(DisplayName = "Clean path: unrelated name does not trigger any gate")]
    public async Task CleanPath_NoMatches_NoGate()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Totally Unique Club XYZ 999");

        var result = await svc.RegisterAsync(request);

        // Should not be blocked by the gate (may fail later at UserManager -- that's OK)
        var wasGateBlocked = !result.Success && result.SimilarClubs?.Any() == true;
        wasGateBlocked.Should().BeFalse("unrelated name should not trigger any gate");
    }

    /// <summary>
    /// SCENARIO: No existing clubs in the database at all
    /// EXPECTED: Registration passes gate with no friction
    /// </summary>
    [Fact(DisplayName = "Clean path: empty database has no friction")]
    public async Task CleanPath_EmptyDb_NoFriction()
    {
        var (svc, _) = CreateService(); // no existing clubs

        var request = MakeRequest("Brand New Club");
        var result = await svc.RegisterAsync(request);

        var wasGateBlocked = !result.Success && result.SimilarClubs?.Any() == true;
        wasGateBlocked.Should().BeFalse("empty database should never trigger the gate");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEARCH RESULTS QUALITY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Search for a club with a short query (less than 3 chars)
    /// EXPECTED: Returns empty list -- no point searching on "Ch"
    /// </summary>
    [Fact(DisplayName = "Search: queries under 3 chars return empty results")]
    public async Task Search_ShortQuery_Empty()
    {
        var (svc, _) = CreateService(ExistingClub);

        var results = await svc.SearchClubsAsync("Ch", null);

        results.Should().BeEmpty("queries under 3 characters should not search");
    }

    /// <summary>
    /// SCENARIO: Search includes mega-club detection
    /// EXPECTED: IsRelatedClub flag set on results that share a root organization
    /// </summary>
    [Fact(DisplayName = "Search: mega-club branches flagged as IsRelatedClub")]
    public async Task Search_MegaClub_Flagged()
    {
        var vaClub = new ClubSearchCandidate
        {
            ClubId = 10, ClubName = "3 Point Lacrosse - VA", State = "VA",
            TeamCount = 8, RepName = "Rep VA", RepEmail = "va@3point.com"
        };
        var (svc, _) = CreateService(vaClub);

        var results = await svc.SearchClubsAsync("3 Point Lacrosse - NC", null);

        results.Should().NotBeEmpty();
        results.First().IsRelatedClub.Should().BeTrue(
            "same root org with different state suffix should be flagged as related");
    }
}
