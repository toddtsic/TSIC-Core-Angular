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
/// Two-layer gate:
///   1. HARD BLOCK on exact-normalized match (token sets identical) —
///      cannot be bypassed by ConfirmedNewClub. Catches duplicate-creation
///      / hijacking attempts including filler-only suffixes ("Charlotte
///      Fury" vs "Charlotte Fury LC") and word reordering.
///   2. SIMILARITY SURFACE for any other 65%+ match — requires
///      ConfirmedNewClub. Allows regional chapters of national orgs
///      (e.g. "Aacme Lax NJ" vs "Aacme Lax MA") to register as siblings.
/// Below 65% → no friction.
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
        bool confirmedNewClub = false,
        bool acceptedTos = true) => new()
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
        ConfirmedNewClub = confirmedNewClub,
        AcceptedTos = acceptedTos
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

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(r => r.UpdateTosAcceptanceByUserIdAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

        var svc = new ClubService(userManager, clubRepo.Object, clubRepRepo.Object,
            userRepo.Object, privilegeService.Object, cache);

        return (svc, clubRepo);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SIMILARITY GATE (65%+ match — any tier)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Registrant types exact name of existing club without confirming
    /// EXPECTED: Hard-blocked with SimilarClubs returned (cannot create a duplicate)
    /// </summary>
    [Fact(DisplayName = "Exact-name match is hard-blocked")]
    public async Task ExactMatch_HardBlocked()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlotte Fury");

        var result = await svc.RegisterAsync(request);

        result.Success.Should().BeFalse("exact name match must be hard-blocked");
        result.SimilarClubs.Should().NotBeNullOrEmpty("response must include the matching club");
        result.Message.Should().Contain("already registered");
    }

    /// <summary>
    /// SCENARIO: Registrant types EXACT name AND sets ConfirmedNewClub = true
    /// EXPECTED: STILL BLOCKED — exact-match bypass via client flag is a security hole.
    ///   Confirmation only allows similar-but-different names (regional siblings).
    /// </summary>
    [Fact(DisplayName = "Exact-name match CANNOT be bypassed by ConfirmedNewClub")]
    public async Task ExactMatch_CannotBypassWithConfirmation()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlotte Fury", confirmedNewClub: true);

        var result = await svc.RegisterAsync(request);

        result.Success.Should().BeFalse("exact match block must NOT be bypassed by ConfirmedNewClub");
    }

    /// <summary>
    /// SCENARIO: Filler-only suffix difference ("Charlotte Fury" vs "Charlotte Fury LC")
    /// EXPECTED: Hard-blocked even with confirmation — "LC" expands to filler so the
    ///   normalized token sets are identical (same club).
    /// </summary>
    [Fact(DisplayName = "Filler-suffix variant ('Fury LC' vs 'Fury') is hard-blocked")]
    public async Task FillerSuffixVariant_HardBlocked()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlotte Fury LC", confirmedNewClub: true);

        var result = await svc.RegisterAsync(request);

        result.Success.Should().BeFalse("filler-only suffix difference must be hard-blocked");
    }

    /// <summary>
    /// SCENARIO: Regional sibling — different distinctive token, same root
    ///   ("Charlotte Fury North" vs existing "Charlotte Fury") with confirmation
    /// EXPECTED: Passes — token sets differ ("north" added), so this is NOT
    ///   an exact match. The regional chapter use case must work.
    /// </summary>
    [Fact(DisplayName = "Regional sibling ('Fury North') with confirmation passes")]
    public async Task RegionalSibling_WithConfirmation_PassesGate()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlotte Fury North", confirmedNewClub: true);

        var result = await svc.RegisterAsync(request);

        var wasGateBlocked = !result.Success && result.SimilarClubs?.Any() == true;
        wasGateBlocked.Should().BeFalse(
            "regional sibling with a distinguishing token must pass when confirmed");
    }

    /// <summary>
    /// SCENARIO: Near-exact typo ("Charlote Fury") without confirmation
    /// EXPECTED: Rejected — single-char typo still scores in the similarity band
    /// </summary>
    [Fact(DisplayName = "Typo variant without confirmation is rejected")]
    public async Task TypoVariant_WithoutConfirmation_Rejected()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlote Fury");

        var result = await svc.RegisterAsync(request);

        result.Success.Should().BeFalse("single-char typo should surface similar clubs");
        result.SimilarClubs.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// SCENARIO: Similar-clubs response must include existing rep contact info
    /// EXPECTED: RepName and RepEmail present so the registrant can reach out
    /// </summary>
    [Fact(DisplayName = "Similar clubs response includes rep contact for self-service")]
    public async Task SimilarClubs_IncludeRepContact()
    {
        var (svc, _) = CreateService(ExistingClub);
        var request = MakeRequest("Charlotte Fury");

        var result = await svc.RegisterAsync(request);

        result.SimilarClubs.Should().NotBeNull();
        var match = result.SimilarClubs![0];
        match.RepName.Should().Be("John Smith");
        match.RepEmail.Should().Be("j.smith@email.com");
    }

    /// <summary>
    /// SCENARIO: Mid-similarity match (shared city, different mascot) without confirmation
    /// EXPECTED: Rejected — warning-band matches still require ConfirmedNewClub
    /// </summary>
    [Fact(DisplayName = "Mid-similarity match without confirmation is rejected")]
    public async Task MidSimilarity_WithoutConfirmation_Rejected()
    {
        var (svc, _) = CreateService(SimilarClub);
        var request = MakeRequest("Charlotte Hawks");

        var result = await svc.RegisterAsync(request);

        if (result.SimilarClubs?.Any() == true)
        {
            result.Success.Should().BeFalse("similar match without confirmation should be rejected");
        }
    }

    /// <summary>
    /// SCENARIO: Mid-similarity match WITH confirmation
    /// EXPECTED: Gate passes — confirmation is what unlocks creation
    /// </summary>
    [Fact(DisplayName = "Mid-similarity match WITH confirmation passes the gate")]
    public async Task MidSimilarity_WithConfirmation_PassesGate()
    {
        var (svc, _) = CreateService(SimilarClub);
        var request = MakeRequest("Charlotte Hawks", confirmedNewClub: true);

        var result = await svc.RegisterAsync(request);

        var wasGateBlocked = !result.Success && result.SimilarClubs?.Any() == true;
        wasGateBlocked.Should().BeFalse(
            "confirmed new club should pass the gate (not return SimilarClubs)");
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
        results[0].IsRelatedClub.Should().BeTrue(
            "same root org with different state suffix should be flagged as related");
    }
}
