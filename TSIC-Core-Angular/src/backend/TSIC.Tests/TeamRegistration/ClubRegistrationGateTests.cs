using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Services.Clubs;
using TSIC.Application.Services.Users;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.Tests.TeamRegistration;

/// <summary>
/// CLUB REP SIGN-UP: WHICH CLUB
///
/// A club name never refuses sign-up (Todd 2026-10-06): a rep replacing their club's old rep must be
/// able to register under the club's own name, so a name another club already uses creates this rep's
/// own club of that name. Two cases resolve to an EXISTING club instead:
///   - the user already reps a club of that name → no second club;
///   - an unclaimed EMPTY club (no reps, no library teams) carries exactly the typed name → claimed
///     silently. A merely normalize-equal name ("True Lacrosse" vs a club named "True") never claims.
/// </summary>
public class ClubRegistrationGateTests
{
    private const string Password = "Password123!";

    // ── Test data ────────────────────────────────────────────────────

    /// <summary>An established club: has a rep and a library.</summary>
    private static readonly ClubSearchCandidate ExistingClub = new()
    {
        ClubId = 1,
        ClubName = "Charlotte Fury",
        State = "NC",
        TeamCount = 12,
        HasRep = true
    };

    /// <summary>Scores in the similarity band against "Charlotte Hawks" — shares "charlotte".</summary>
    private static readonly ClubSearchCandidate SimilarClub = new()
    {
        ClubId = 2,
        ClubName = "Charlotte Eagles",
        State = "NC",
        TeamCount = 5,
        HasRep = true
    };

    private static ClubSearchCandidate EmptyShell(int clubId, string name) => new()
    {
        ClubId = clubId,
        ClubName = name,
        TeamCount = 0,
        HasRep = false
    };

    private static ClubRepRegistrationRequest MakeRequest(
        string clubName,
        string? username = null,
        bool acceptedTos = true) => new()
        {
            ClubName = clubName,
            FirstName = "Test",
            LastName = "User",
            Gender = "M",
            Email = "test@example.com",
            Username = username ?? "testuser_" + Guid.NewGuid().ToString("N")[..8],
            Password = Password,
            StreetAddress = "123 Main St",
            City = "Anytown",
            State = "NC",
            PostalCode = "28205",
            Cellphone = "5551234567",
            AcceptedTos = acceptedTos
        };

    // ── Service factory ─────────────────────────────────────────────

    private sealed class Fixture
    {
        public required ClubService Svc { get; init; }
        public required Mock<IClubRepository> ClubRepo { get; init; }
        public required Mock<IClubRepRepository> ClubRepRepo { get; init; }
        public required Mock<IRegistrationRepository> Registrations { get; init; }
        public required Mock<IClubTeamRepository> ClubTeams { get; init; }

        /// <summary>The service created a NEW Clubs row.</summary>
        public void VerifyNewClubCreated() =>
            ClubRepo.Verify(r => r.Add(It.IsAny<Clubs>()), Times.Once);

        /// <summary>The service created no Clubs row (resolved to an existing club).</summary>
        public void VerifyNoClubCreated() =>
            ClubRepo.Verify(r => r.Add(It.IsAny<Clubs>()), Times.Never);
    }

    /// <param name="existingUser">When set, the request's username resolves to this account (password = <see cref="Password"/>).</param>
    /// <param name="existingUsersClubs">The clubs that existing account already reps.</param>
    /// <param name="unclaimedEmptyClubIds">Clubs IsUnclaimedEmptyAsync confirms on the write.</param>
    private static Fixture CreateService(
        ClubSearchCandidate[] existingClubs,
        ApplicationUser? existingUser = null,
        ClubWithUsageInfo[]? existingUsersClubs = null,
        int[]? unclaimedEmptyClubIds = null,
        Registrations[]? teamlessRegistrations = null,
        Dictionary<int, ClubTeams[]>? libraries = null)
    {
        var clubRepo = new Mock<IClubRepository>();
        clubRepo.Setup(r => r.GetSearchCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingClubs.ToList());
        var unclaimed = (unclaimedEmptyClubIds ?? []).ToHashSet();
        clubRepo.Setup(r => r.IsUnclaimedEmptyAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => unclaimed.Contains(id));

        var clubRepRepo = new Mock<IClubRepRepository>();
        clubRepRepo.Setup(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(false);
        clubRepRepo.Setup(r => r.GetClubsForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingUsersClubs ?? []).ToList());

        // UserManager requires a store mock that implements IUserPasswordStore
        var hasher = new PasswordHasher<ApplicationUser>();
        var userStore = new Mock<IUserPasswordStore<ApplicationUser>>();
        userStore.As<IUserStore<ApplicationUser>>();
        userStore.Setup(s => s.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);
        userStore.Setup(s => s.SetPasswordHashAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        userStore.Setup(s => s.HasPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        if (existingUser != null)
        {
            userStore.Setup(s => s.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingUser);
            userStore.Setup(s => s.GetPasswordHashAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(hasher.HashPassword(existingUser, Password));
        }
        else
        {
            userStore.Setup(s => s.GetPasswordHashAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("hashed");
        }
        var userManager = new UserManager<ApplicationUser>(
            userStore.Object, null!, hasher,
            null!, null!, null!, null!, null!, null!);

        var privilegeService = new Mock<IUserPrivilegeLevelService>();
        privilegeService.Setup(p => p.ValidatePrivilegeForRegistrationAsync(
            It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var registrations = new Mock<IRegistrationRepository>();
        registrations.Setup(r => r.GetTeamlessClubRepRegistrationsByClubNameAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((teamlessRegistrations ?? []).ToList());

        // Each club's library, by ClubId; a club looked up by id is the matching search candidate.
        var clubTeams = new Mock<IClubTeamRepository>();
        clubTeams.Setup(r => r.GetByClubIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => (libraries ?? []).TryGetValue(id, out var lib) ? lib.ToList() : []);
        clubTeams.Setup(r => r.GetByClubIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<int> ids, CancellationToken _) =>
                ids.SelectMany(id => (libraries ?? []).TryGetValue(id, out var lib) ? lib : []).ToList());
        clubTeams.Setup(r => r.GetLastRegisteredByClubIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, DateTime>());
        clubRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => existingClubs.Where(c => c.ClubId == id)
                .Select(c => new Clubs { ClubId = c.ClubId, ClubName = c.ClubName }).FirstOrDefault());

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(r => r.UpdateTosAcceptanceByUserIdAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

        var userProfileService = new Mock<IUserProfileService>();

        var svc = new ClubService(userManager, clubRepo.Object, clubRepRepo.Object, registrations.Object, clubTeams.Object,
            userRepo.Object, privilegeService.Object, userProfileService.Object, cache);

        return new Fixture { Svc = svc, ClubRepo = clubRepo, ClubRepRepo = clubRepRepo, Registrations = registrations, ClubTeams = clubTeams };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  A NAME NEVER REFUSES — the rep gets their own club of that name
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: A new rep types the exact name of an established club (it has a rep and teams) —
    ///   typically the rep replacing that club's old rep.
    /// EXPECTED: Sign-up succeeds and creates the rep's own club of that name. Never attached to the
    ///   established club: that would hand a stranger its library.
    /// </summary>
    [Fact(DisplayName = "Exact name of an established club: own club created, never refused")]
    public async Task ExactMatch_CreatesOwnClub()
    {
        var f = CreateService([ExistingClub]);

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlotte Fury"));

        result.Success.Should().BeTrue("a club name never refuses sign-up");
        f.VerifyNewClubCreated();
        result.ClubId.Should().NotBe(ExistingClub.ClubId, "an established club is never joined at sign-up");
    }

    /// <summary>
    /// SCENARIO: Filler-only suffix difference ("Charlotte Fury LC" vs "Charlotte Fury")
    /// EXPECTED: Same as an exact name — succeeds with the rep's own club.
    /// </summary>
    [Fact(DisplayName = "Filler-suffix variant ('Fury LC'): own club created")]
    public async Task FillerSuffixVariant_CreatesOwnClub()
    {
        var f = CreateService([ExistingClub]);

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlotte Fury LC"));

        result.Success.Should().BeTrue();
        f.VerifyNewClubCreated();
    }

    /// <summary>
    /// SCENARIO: Near-exact typo ("Charlote Fury")
    /// EXPECTED: Succeeds — no confirmation step any more.
    /// </summary>
    [Fact(DisplayName = "Typo variant: own club created, no confirmation needed")]
    public async Task TypoVariant_CreatesOwnClub()
    {
        var f = CreateService([ExistingClub]);

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlote Fury"));

        result.Success.Should().BeTrue();
        f.VerifyNewClubCreated();
    }

    /// <summary>
    /// SCENARIO: Mid-similarity match (shared city, different mascot)
    /// EXPECTED: Succeeds — no confirmation step any more.
    /// </summary>
    [Fact(DisplayName = "Mid-similarity name: own club created, no confirmation needed")]
    public async Task MidSimilarity_CreatesOwnClub()
    {
        var f = CreateService([SimilarClub]);

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlotte Hawks"));

        result.Success.Should().BeTrue();
        f.VerifyNewClubCreated();
    }

    /// <summary>
    /// SCENARIO: Regional sibling ("Charlotte Fury North" vs existing "Charlotte Fury")
    /// EXPECTED: Succeeds with the rep's own club.
    /// </summary>
    [Fact(DisplayName = "Regional sibling ('Fury North'): own club created")]
    public async Task RegionalSibling_CreatesOwnClub()
    {
        var f = CreateService([ExistingClub]);

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlotte Fury North"));

        result.Success.Should().BeTrue();
        f.VerifyNewClubCreated();
    }

    [Fact(DisplayName = "Clean path: unrelated name creates a club")]
    public async Task CleanPath_NoMatches_CreatesClub()
    {
        var f = CreateService([ExistingClub]);

        var result = await f.Svc.RegisterAsync(MakeRequest("Totally Unique Club XYZ 999"));

        result.Success.Should().BeTrue();
        f.VerifyNewClubCreated();
    }

    [Fact(DisplayName = "Clean path: empty database creates a club")]
    public async Task CleanPath_EmptyDb_CreatesClub()
    {
        var f = CreateService([]);

        var result = await f.Svc.RegisterAsync(MakeRequest("Brand New Club"));

        result.Success.Should().BeTrue();
        f.VerifyNewClubCreated();
    }

    [Fact(DisplayName = "Terms of Service not accepted: refused")]
    public async Task TosNotAccepted_Refused()
    {
        var f = CreateService([]);

        var result = await f.Svc.RegisterAsync(MakeRequest("Brand New Club", acceptedTos: false));

        result.Success.Should().BeFalse();
        f.VerifyNoClubCreated();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SILENT CLAIM of an unclaimed empty club
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: An admin provisioned "Charlotte Fury" (no rep, no library) and the rep types exactly that name.
    /// EXPECTED: The rep is attached to that club — no duplicate created, no question asked.
    /// </summary>
    [Fact(DisplayName = "Empty club with the exact typed name is claimed silently")]
    public async Task EmptyShell_ExactName_ClaimedSilently()
    {
        var shell = EmptyShell(7, "Charlotte Fury");
        var f = CreateService([shell], unclaimedEmptyClubIds: [7]);

        var result = await f.Svc.RegisterAsync(MakeRequest("charlotte fury "));

        result.Success.Should().BeTrue();
        result.ClubId.Should().Be(7);
        f.VerifyNoClubCreated();
    }

    /// <summary>
    /// SCENARIO: An empty club named "True" exists; the rep types "True Lacrosse" — normalize-equal
    ///   ("lacrosse" is filler) but not the club's name.
    /// EXPECTED: Not claimed — a new club is created. You must type a shell's real name to get it.
    /// </summary>
    [Fact(DisplayName = "Empty club that only NORMALIZES to the typed name is not claimed")]
    public async Task EmptyShell_NormalizedOnly_NotClaimed()
    {
        var shell = EmptyShell(8, "True");
        var f = CreateService([shell], unclaimedEmptyClubIds: [8]);

        var result = await f.Svc.RegisterAsync(MakeRequest("True Lacrosse"));

        result.Success.Should().BeTrue();
        result.ClubId.Should().NotBe(8);
        f.VerifyNewClubCreated();
    }

    /// <summary>
    /// SCENARIO: The search still shows the club as empty, but by the write it has a rep or teams
    ///   (IsUnclaimedEmptyAsync, the gate, says no).
    /// EXPECTED: Not claimed — a new club is created.
    /// </summary>
    [Fact(DisplayName = "Empty-looking club that is no longer empty at the write is not claimed")]
    public async Task EmptyShell_NoLongerEmpty_NotClaimed()
    {
        var shell = EmptyShell(9, "Charlotte Fury");
        var f = CreateService([shell], unclaimedEmptyClubIds: []);

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlotte Fury"));

        result.Success.Should().BeTrue();
        result.ClubId.Should().NotBe(9);
        f.VerifyNewClubCreated();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SAME USER, SAME CLUB NAME — no second club
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: An existing account that already reps "Charlotte Fury" signs up again with "Charlotte Fury LC".
    /// EXPECTED: No second club — resolves to the club they already rep.
    /// </summary>
    [Fact(DisplayName = "Existing user who already reps that club name gets no second club")]
    public async Task ExistingUser_SameNameClub_NoSecondClub()
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "jane" };
        var own = new ClubWithUsageInfo { ClubId = 1, ClubName = "Charlotte Fury", IsInUse = true };
        var f = CreateService([ExistingClub], existingUser: user, existingUsersClubs: [own]);
        f.ClubRepRepo.Setup(r => r.ExistsAsync("user-1", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlotte Fury LC", username: "jane"));

        result.Success.Should().BeTrue();
        result.ClubId.Should().Be(1);
        f.VerifyNoClubCreated();
        f.ClubRepRepo.Verify(r => r.Add(It.IsAny<ClubReps>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEARCH RESULTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Search: queries under 3 chars return empty results")]
    public async Task Search_ShortQuery_Empty()
    {
        var f = CreateService([ExistingClub]);

        var results = await f.Svc.SearchClubsAsync("Ch", null);

        results.Should().BeEmpty("queries under 3 characters should not search");
    }

    [Fact(DisplayName = "Search: mega-club branches flagged as IsRelatedClub")]
    public async Task Search_MegaClub_Flagged()
    {
        var vaClub = new ClubSearchCandidate
        {
            ClubId = 10,
            ClubName = "3 Point Lacrosse - VA",
            State = "VA",
            TeamCount = 8,
            HasRep = true
        };
        var f = CreateService([vaClub]);

        var results = await f.Svc.SearchClubsAsync("3 Point Lacrosse - NC", null);

        results.Should().NotBeEmpty();
        results[0].IsRelatedClub.Should().BeTrue(
            "same root org with different state suffix should be flagged as related");
    }

    [Fact(DisplayName = "Search: an exact-name club is flagged, a claimable one marked claimable")]
    public async Task Search_FlagsExactAndClaimable()
    {
        var f = CreateService([ExistingClub, EmptyShell(7, "Charlotte Fury Gold")]);

        var results = await f.Svc.SearchClubsAsync("Charlotte Fury", null);

        results.Single(r => r.ClubId == 1).IsExactMatch.Should().BeTrue();
        results.Single(r => r.ClubId == 1).IsClaimable.Should().BeFalse("it has a rep and teams");
        results.Single(r => r.ClubId == 7).IsClaimable.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RENAME — teamless event registrations take the new name
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: A rep with no teams yet renames their club while already registered in an event.
    /// EXPECTED: the event registration's club name follows the rename; Assignment and the category
    /// follow only where they carried the old name; a hand-edited value is left alone.
    /// </summary>
    [Fact(DisplayName = "Rename: a teamless event registration takes the new club name")]
    public async Task Rename_RestampsTeamlessRegistrations()
    {
        var own = new ClubWithUsageInfo { ClubId = 1, ClubName = "Old Name", IsInUse = false };
        var stamped = new Registrations
        {
            ClubName = "Old Name", Assignment = "Old Name", RegistrationCategory = "Club Rep: Old Name"
        };
        var handEdited = new Registrations
        {
            ClubName = "Old Name", Assignment = "Director note", RegistrationCategory = "Other"
        };
        var f = CreateService([], existingUsersClubs: [own], teamlessRegistrations: [stamped, handEdited]);
        f.ClubRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clubs { ClubId = 1, ClubName = "Old Name" });

        var result = await f.Svc.RenameClubAsync("user-1",
            new ClubRenameRequest { CurrentClubName = "Old Name", NewClubName = "New Name" });

        result.Success.Should().BeTrue();
        stamped.ClubName.Should().Be("New Name");
        stamped.Assignment.Should().Be("New Name");
        stamped.RegistrationCategory.Should().Be("Club Rep: New Name");
        handEdited.ClubName.Should().Be("New Name");
        handEdited.Assignment.Should().Be("Director note");
        handEdited.RegistrationCategory.Should().Be("Other");
        f.ClubRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  "THIS IS MY CLUB" — the picked club's teams are copied
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: A rep taking over Charlotte Fury picks the existing Charlotte Fury as their club.
    /// EXPECTED: their own club is created, and the picked club's active teams are copied into it.
    /// </summary>
    [Fact(DisplayName = "Picked club: its active teams are copied into the rep's new club")]
    public async Task PickedClub_TeamsCopied()
    {
        var library = new[]
        {
            new ClubTeams { ClubTeamId = 10, ClubId = 1, ClubTeamName = "2030 Blue", ClubTeamGradYear = "2030", Active = true },
            new ClubTeams { ClubTeamId = 11, ClubId = 1, ClubTeamName = "2029 Gold", ClubTeamGradYear = "2029", Active = true },
            new ClubTeams { ClubTeamId = 12, ClubId = 1, ClubTeamName = "2027 Old", ClubTeamGradYear = "2027", Active = false },
        };
        var f = CreateService([ExistingClub], libraries: new() { [1] = library });

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlotte Fury") with { SourceClubId = 1 });

        result.Success.Should().BeTrue();
        f.VerifyNewClubCreated();
        f.ClubTeams.Verify(r => r.Add(It.Is<ClubTeams>(t => t.ClubId != 1 && t.ClubTeamId == 0 && t.Active)), Times.Exactly(2));
        f.ClubTeams.Verify(r => r.Add(It.Is<ClubTeams>(t => t.ClubTeamName == "2027 Old")), Times.Never);
        f.ClubTeams.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// SCENARIO: The rep answers "None of these — we're a new club".
    /// EXPECTED: their own club, empty.
    /// </summary>
    [Fact(DisplayName = "None of these: no teams copied")]
    public async Task NoneOfThese_NothingCopied()
    {
        var library = new[] { new ClubTeams { ClubTeamId = 10, ClubId = 1, ClubTeamName = "2030 Blue", ClubTeamGradYear = "2030", Active = true } };
        var f = CreateService([ExistingClub], libraries: new() { [1] = library });

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlotte Fury"));

        result.Success.Should().BeTrue();
        f.ClubTeams.Verify(r => r.Add(It.IsAny<ClubTeams>()), Times.Never);
    }

    /// <summary>
    /// SCENARIO: The request names a club whose name is NOT the club name typed (a forged or stale id).
    /// EXPECTED: refused before any account or club is created — an id never copies an unrelated library.
    /// </summary>
    [Fact(DisplayName = "Picked club of another name: refused, nothing created or copied")]
    public async Task PickedClub_OtherName_Refused()
    {
        var library = new[] { new ClubTeams { ClubTeamId = 50, ClubId = 5, ClubTeamName = "Storm 2030", ClubTeamGradYear = "2030", Active = true } };
        var storm = new ClubSearchCandidate { ClubId = 5, ClubName = "Raleigh Storm", State = "NC", TeamCount = 1, HasRep = true };
        var f = CreateService([ExistingClub, storm], libraries: new() { [5] = library });

        var result = await f.Svc.RegisterAsync(MakeRequest("Charlotte Fury") with { SourceClubId = 5 });

        result.Success.Should().BeFalse();
        f.VerifyNoClubCreated();
        f.ClubTeams.Verify(r => r.Add(It.IsAny<ClubTeams>()), Times.Never);
    }
}
