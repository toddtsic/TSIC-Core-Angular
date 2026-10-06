using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using System.Transactions;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Contracts.Repositories;
using TSIC.Application.Services.Users;
using TSIC.Application.Services.Clubs;
using TSIC.Application.Services.Shared.Mapping;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Clubs;

public sealed class ClubService : IClubService
{
    private const string ClubCacheKey = "clubs:search_candidates";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IClubRepository _clubRepo;
    private readonly IClubRepRepository _clubRepRepo;
    private readonly IUserRepository _userRepo;
    private readonly IUserPrivilegeLevelService _privilegeService;
    private readonly IUserProfileService _userProfileService;
    private readonly IMemoryCache _cache;

    public ClubService(
        UserManager<ApplicationUser> userManager,
        IClubRepository clubRepo,
        IClubRepRepository clubRepRepo,
        IUserRepository userRepo,
        IUserPrivilegeLevelService privilegeService,
        IUserProfileService userProfileService,
        IMemoryCache cache)
    {
        _userManager = userManager;
        _clubRepo = clubRepo;
        _clubRepRepo = clubRepRepo;
        _userRepo = userRepo;
        _privilegeService = privilegeService;
        _userProfileService = userProfileService;
        _cache = cache;
    }

    /// <summary>
    /// Register a new club rep account. A club name never refuses the sign-up (Todd 2026-10-06):
    /// a rep replacing their club's old rep must be able to register under the club's own name,
    /// so a name another club already uses creates this rep's own club of that name. Two
    /// exceptions resolve to an EXISTING club instead of a new one:
    /// - the user already reps a club of that name → no second club (their own screens find
    ///   "your club" by name, and two of one name would be indistinguishable to them);
    /// - an unclaimed empty club carries exactly the typed name → claimed silently.
    /// </summary>
    public async Task<ClubRepRegistrationResponse> RegisterAsync(ClubRepRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClubName) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return new ClubRepRegistrationResponse { Success = false, ClubId = null, UserId = null, Message = "Club name, username, and password are required" };
        }

        if (!request.AcceptedTos)
        {
            return new ClubRepRegistrationResponse { Success = false, ClubId = null, UserId = null, Message = "You must accept the Terms of Service." };
        }

        // ── Validate user account ───────────────────────────────────────

        var existingUser = await _userManager.FindByNameAsync(request.Username);

        if (existingUser != null)
        {
            var isValid = await _privilegeService.ValidatePrivilegeForRegistrationAsync(existingUser.Id, RoleConstants.ClubRep);
            if (!isValid)
            {
                var existingPrivilege = await _privilegeService.GetUserPrivilegeLevelAsync(existingUser.Id);
                var privilegeName = PrivilegeNameMapper.GetPrivilegeName(existingPrivilege);
                return new ClubRepRegistrationResponse
                {
                    Success = false,
                    ClubId = null,
                    UserId = null,
                    Message = $"This account is locked to {privilegeName} privilege level. To protect player data, one account can only be used for one privilege level. Please use a different email address and username for Club Rep registration."
                };
            }

            var passwordValid = await _userManager.CheckPasswordAsync(existingUser, request.Password);
            if (!passwordValid)
            {
                return new ClubRepRegistrationResponse
                {
                    Success = false,
                    ClubId = null,
                    UserId = null,
                    Message = "Invalid password for existing account."
                };
            }
        }

        // ── Which club ─────────────────────────────────────────────────
        //
        // A name collision never refuses (see the summary). The duplicate risk it reopens — two
        // reps of one club registering the same teams — is surfaced where teams are entered (the
        // team wizard's same-name notice) and on the director's CADT tree, not blocked here.

        int clubId = 0; // sentinel: create new

        // The user already reps a club of this name: no second one. Normalized, so "Fury LC" and
        // "Fury Lacrosse" are the same club to them, exactly as sign-up's search treats them.
        if (existingUser != null)
        {
            var ownClubs = await _clubRepRepo.GetClubsForUserAsync(existingUser.Id);
            var ownSameName = ownClubs.FirstOrDefault(c =>
                ClubNameMatcher.IsSameClubName(c.ClubName, request.ClubName));
            if (ownSameName != null)
            {
                clubId = ownSameName.ClubId;
            }
        }

        // An unclaimed EMPTY club — no reps AND no library teams — carrying exactly the typed name
        // is claimed instead of duplicated (Todd 2026-10-06: silently). Safe because a wrong
        // claimant inherits nothing: no teams, no rosters, no history. The typed name must BE the
        // club's name, not merely normalize to it: filler words ("lacrosse", "lc", "club") collapse
        // under normalization, so "True Lacrosse" must never claim a shell named "True". The
        // search's IsClaimable is display data; IsUnclaimedEmptyAsync is the gate.
        if (clubId == 0)
        {
            var typed = request.ClubName.Trim();
            var shell = (await SearchClubsAsync(typed, null)).FirstOrDefault(c =>
                c.IsExactMatch
                && c.IsClaimable
                && string.Equals(c.ClubName.Trim(), typed, StringComparison.OrdinalIgnoreCase));
            if (shell != null && await _clubRepo.IsUnclaimedEmptyAsync(shell.ClubId))
            {
                clubId = shell.ClubId;
            }
        }

        // ── Create user + club/link inside transaction ──────────────────

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        ApplicationUser user;
        if (existingUser == null)
        {
            user = new ApplicationUser
            {
                UserName = request.Username,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Gender = request.Gender, // collected on the club-rep form (M/F)
                Cellphone = request.Cellphone,
                Phone = request.Cellphone,
                StreetAddress = request.StreetAddress,
                City = request.City,
                State = request.State,
                PostalCode = request.PostalCode,
                Modified = DateTime.Now
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                var msg = string.Join("; ", createResult.Errors.Select(e => e.Description));
                return new ClubRepRegistrationResponse { Success = false, ClubId = null, UserId = null, Message = msg };
            }
        }
        else
        {
            user = existingUser;
        }

        // Persist ToS acceptance (bTSICWaiverSigned + TSICWaiverSigned_TS).
        // Matches AdultRegistrationService pattern so any flow that checks
        // RequiresTosSignatureAsync sees this rep as signed.
        await _userRepo.UpdateTosAcceptanceByUserIdAsync(user.Id);

        if (clubId == 0)
        {
            // Create new club
            var club = new Domain.Entities.Clubs
            {
                ClubName = request.ClubName,
                LebUserId = user.Id,
                Modified = DateTime.Now
            };
            _clubRepo.Add(club);
            await _clubRepo.SaveChangesAsync();
            clubId = club.ClubId;
            InvalidateSearchCache();
        }

        // Check if rep link already exists (e.g. user re-registering for same club)
        var alreadyLinked = await _clubRepRepo.ExistsAsync(user.Id, clubId);
        if (!alreadyLinked)
        {
            var clubRep = new ClubReps
            {
                ClubId = clubId,
                ClubRepUserId = user.Id
            };
            _clubRepRepo.Add(clubRep);
            await _clubRepRepo.SaveChangesAsync();
        }

        scope.Complete();

        return new ClubRepRegistrationResponse
        {
            Success = true,
            ClubId = clubId,
            UserId = user.Id,
            Message = null
        };
    }

    public async Task<AddClubResponse> AddClubAsync(AddClubRequest request, string userId)
    {
        // If user confirmed they want to use existing club
        if (request.UseExistingClubId.HasValue)
        {
            var existingClub = await _clubRepo.GetByIdAsync(request.UseExistingClubId.Value);
            if (existingClub == null)
            {
                return new AddClubResponse
                {
                    Success = false,
                    Message = "Selected club not found",
                    ClubRepId = null,
                    ClubId = null,
                    SimilarClubs = null
                };
            }

            var alreadyExists = await _clubRepRepo.ExistsAsync(userId, request.UseExistingClubId.Value);
            if (alreadyExists)
            {
                return new AddClubResponse
                {
                    Success = false,
                    Message = "You already have access to this club",
                    ClubRepId = null,
                    ClubId = request.UseExistingClubId.Value,
                    SimilarClubs = null
                };
            }

            var clubRep = new ClubReps
            {
                ClubId = request.UseExistingClubId.Value,
                ClubRepUserId = userId
            };
            _clubRepRepo.Add(clubRep);
            await _clubRepRepo.SaveChangesAsync();

            return new AddClubResponse
            {
                Success = true,
                Message = "Club added successfully",
                ClubRepId = clubRep.Aid,
                ClubId = existingClub.ClubId,
                SimilarClubs = null
            };
        }

        // Check for similar clubs
        var similarClubs = await SearchClubsAsync(request.ClubName, null);

        // User wants to create new club
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return new AddClubResponse
            {
                Success = false,
                Message = "User not found",
                ClubRepId = null,
                ClubId = null,
                SimilarClubs = null
            };
        }

        var club = new Domain.Entities.Clubs
        {
            ClubName = request.ClubName,
            LebUserId = user.Id
        };
        _clubRepo.Add(club);
        await _clubRepo.SaveChangesAsync();
        InvalidateSearchCache();

        var newClubRep = new ClubReps
        {
            ClubId = club.ClubId,
            ClubRepUserId = userId
        };
        _clubRepRepo.Add(newClubRep);
        await _clubRepRepo.SaveChangesAsync();

        return new AddClubResponse
        {
            Success = true,
            Message = "New club created and added successfully",
            ClubRepId = newClubRep.Aid,
            ClubId = club.ClubId,
            SimilarClubs = similarClubs.Count > 0 ? similarClubs : null
        };
    }

    /// <summary>
    /// Search clubs using composite scoring (Levenshtein + token/Jaccard).
    /// Results include mega-club detection via IsRelatedClub flag.
    /// Cached for 5 minutes to support live typeahead without hammering the DB.
    /// </summary>
    public async Task<List<ClubSearchResult>> SearchClubsAsync(string query, string? state)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
        {
            return new List<ClubSearchResult>();
        }

        var candidates = await GetCachedCandidatesAsync();

        var results = candidates
            .Select(c =>
            {
                var compositeScore = ClubNameMatcher.CalculateCompositeScore(query, c.ClubName);
                var isRelated = ClubNameMatcher.AreRelatedClubs(query, c.ClubName);
                var isExact = ClubNameMatcher.IsExactNormalizedMatch(query, c.ClubName);

                return new ClubSearchResult
                {
                    ClubId = c.ClubId,
                    ClubName = c.ClubName,
                    State = c.State,
                    TeamCount = c.TeamCount,
                    MatchScore = compositeScore,
                    IsRelatedClub = isRelated,
                    IsExactMatch = isExact,
                    // Mirrors IClubRepository.IsUnclaimedEmptyAsync — no reps AND an empty
                    // library. TeamCount is the ClubTeams count, so the two agree by
                    // construction. The server re-checks on the write; this is display only.
                    IsClaimable = !c.HasRep && c.TeamCount == 0
                };
            })
            .Where(r => r.MatchScore >= 65 || r.IsRelatedClub)
            .OrderByDescending(r => r.MatchScore)
            .Take(10)
            .ToList();

        return results;
    }

    // Self-profile read/write is role-neutral ApplicationUser mutation — owned by
    // IUserProfileService and shared with the adult-registration wizard. These two
    // methods delegate + map to/from the club-rep DTO shape so the api/club-reps/me
    // contract is unchanged.
    public async Task<ClubRepProfileDto?> GetSelfProfileAsync(string userId)
    {
        var profile = await _userProfileService.GetSelfProfileAsync(userId);
        if (profile == null)
        {
            return null;
        }

        return new ClubRepProfileDto
        {
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Email = profile.Email,
            Cellphone = profile.Cellphone,
            StreetAddress = profile.StreetAddress,
            City = profile.City,
            State = profile.State,
            PostalCode = profile.PostalCode
        };
    }

    public Task<bool> UpdateSelfProfileAsync(string userId, ClubRepProfileUpdateRequest request)
    {
        return _userProfileService.UpdateSelfProfileAsync(userId, new UserProfileUpdateRequest
        {
            Email = request.Email,
            Cellphone = request.Cellphone,
            StreetAddress = request.StreetAddress,
            City = request.City,
            State = request.State,
            PostalCode = request.PostalCode
        });
    }

    /// <summary>
    /// Rename a club the caller reps (the library name — Clubs.ClubName). Locked once the club has
    /// registered teams (IsInUse, found by id). Inside an event the club's name is the club rep
    /// registration's club_name, which this never touches; a Director or Superuser renames that per event.
    /// </summary>
    public async Task<ClubRenameResponse> RenameClubAsync(string userId, ClubRenameRequest request)
    {
        var current = (request.CurrentClubName ?? string.Empty).Trim();
        var next = (request.NewClubName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(next))
        {
            return new ClubRenameResponse { Success = false, Message = "Club name is required." };
        }

        // Resolve which of the caller's clubs to rename (also confirms membership).
        var myClubs = await _clubRepRepo.GetClubsForUserAsync(userId);
        var target = myClubs.FirstOrDefault(c =>
            string.Equals(c.ClubName, current, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            return new ClubRenameResponse { Success = false, Message = "Club not found for your account." };
        }

        // No-op (identical name) — accept without a write.
        if (string.Equals(target.ClubName, next, StringComparison.Ordinal))
        {
            return new ClubRenameResponse { Success = true, NewClubName = target.ClubName };
        }

        // Guard: a club with registered teams is locked.
        if (target.IsInUse)
        {
            return new ClubRenameResponse
            {
                Success = false,
                Message = "This club already has registered teams, so its name is locked."
            };
        }

        // Another club's name is allowed, as at sign-up (Todd 2026-10-06). The one collision refused
        // is with ANOTHER of this rep's own clubs: their screens find "your club" by name, and two of
        // one name would be indistinguishable to them. Normalized, matching sign-up's same-rep guard.
        var ownCollision = myClubs.FirstOrDefault(c =>
            c.ClubId != target.ClubId && ClubNameMatcher.IsSameClubName(c.ClubName, next));
        if (ownCollision != null)
        {
            return new ClubRenameResponse
            {
                Success = false,
                Message = $"You already represent a club named \"{ownCollision.ClubName}\"."
            };
        }

        var club = await _clubRepo.GetByIdAsync(target.ClubId);
        if (club == null)
        {
            return new ClubRenameResponse { Success = false, Message = "Club not found." };
        }

        club.ClubName = next;
        club.LebUserId = userId;
        club.Modified = DateTime.Now;
        await _clubRepo.SaveChangesAsync();
        InvalidateSearchCache();

        return new ClubRenameResponse { Success = true, NewClubName = next };
    }

    public void InvalidateSearchCache()
    {
        _cache.Remove(ClubCacheKey);
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private async Task<List<ClubSearchCandidate>> GetCachedCandidatesAsync()
    {
        if (_cache.TryGetValue(ClubCacheKey, out List<ClubSearchCandidate>? cached) && cached != null)
        {
            return cached;
        }

        var candidates = await _clubRepo.GetSearchCandidatesAsync();
        _cache.Set(ClubCacheKey, candidates, CacheTtl);
        return candidates;
    }
}
