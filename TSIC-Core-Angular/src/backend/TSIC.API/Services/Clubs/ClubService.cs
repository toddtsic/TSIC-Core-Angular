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
    /// Register a new club rep account. Enforces a strict gate:
    /// - If ExistingClubId is set → link user to that club (no new club created)
    /// - If ConfirmedNewClub is true → create new club
    /// - If neither is set → run fuzzy search; if matches found, return them
    ///   with Success=false and DO NOT create anything. Caller must decide first.
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
                    Success = false, ClubId = null, UserId = null,
                    Message = $"This account is locked to {privilegeName} privilege level. To protect player data, one account can only be used for one privilege level. Please use a different email address and username for Club Rep registration."
                };
            }

            var passwordValid = await _userManager.CheckPasswordAsync(existingUser, request.Password);
            if (!passwordValid)
            {
                return new ClubRepRegistrationResponse
                {
                    Success = false, ClubId = null, UserId = null,
                    Message = "Invalid password for existing account."
                };
            }
        }

        // ── Club name gate ─────────────────────────────────────────────
        //
        // Two layers:
        //  1. HARD BLOCK on exact-normalized match (token sets identical):
        //     catches the duplicate-creation / hijacking scenario. Cannot
        //     be bypassed by ConfirmedNewClub. Covers exact text, case &
        //     whitespace differences, filler-only suffixes ("Charlotte
        //     Fury LC"), and word reordering ("Lions Aacme" vs "Aacme Lions").
        //  2. SIMILARITY SURFACE for any 65%+ non-exact match: requires
        //     ConfirmedNewClub. Allows regional chapters of national orgs
        //     (e.g. "Aacme Lax NJ" vs "Aacme Lax MA") to register as siblings.

        var similarClubs = await SearchClubsAsync(request.ClubName, null);
        var exactMatch = similarClubs.FirstOrDefault(c => c.IsExactMatch);

        if (exactMatch != null)
        {
            return new ClubRepRegistrationResponse
            {
                Success = false, ClubId = null, UserId = null,
                Message = $"A club named \"{exactMatch.ClubName}\" is already registered. "
                        + "If this is your club, please contact the existing rep to be added. "
                        + "If you're a different chapter, register with a name that distinguishes "
                        + "your region (e.g. add a state suffix).",
                SimilarClubs = similarClubs
            };
        }

        var nearMatches = similarClubs.Where(c => c.MatchScore >= 65).ToList();

        if (nearMatches.Count > 0 && !request.ConfirmedNewClub)
        {
            return new ClubRepRegistrationResponse
            {
                Success = false, ClubId = null, UserId = null,
                Message = "We found clubs with similar names. If none of these are yours, confirm below to create a new club.",
                SimilarClubs = nearMatches
            };
        }

        // Either no matches or caller confirmed new club in warning band
        int clubId = 0; // sentinel: create new

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
                    RepName = c.RepName,
                    RepEmail = c.RepEmail
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
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Cellphone = request.Cellphone,
            StreetAddress = request.StreetAddress,
            City = request.City,
            State = request.State,
            PostalCode = request.PostalCode
        });
    }

    /// <summary>
    /// Rename a club the caller reps, guarded to the data-safe window. Because
    /// IsInUse is computed by matching the club's name against Registrations.club_name,
    /// IsInUse=false guarantees no team/registration copies reference the old name —
    /// so renaming the single Clubs row can't orphan anything. Once a team exists the
    /// name is locked here (a true rename then becomes a deliberate admin operation).
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

        // Guard: a club with registered teams is locked — renaming would strand the
        // Registrations.club_name copies that drive usage/library matching.
        if (target.IsInUse)
        {
            return new ClubRenameResponse
            {
                Success = false,
                Message = "This club already has registered teams, so its name is locked."
            };
        }

        // Collision: don't rename into an existing club (exact-normalized match),
        // mirroring the create flow's hard block.
        var matches = await SearchClubsAsync(next, null);
        var collision = matches.FirstOrDefault(m => m.IsExactMatch && m.ClubId != target.ClubId);
        if (collision != null)
        {
            return new ClubRenameResponse
            {
                Success = false,
                Message = $"A club named \"{collision.ClubName}\" already exists."
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
