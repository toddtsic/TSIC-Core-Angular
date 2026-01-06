using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IClubRepository _clubRepo;
    private readonly IClubRepRepository _clubRepRepo;
    private readonly IUserPrivilegeLevelService _privilegeService;

    public ClubService(
        UserManager<ApplicationUser> userManager,
        IClubRepository clubRepo,
        IClubRepRepository clubRepRepo,
        IUserPrivilegeLevelService privilegeService)
    {
        _userManager = userManager;
        _clubRepo = clubRepo;
        _clubRepRepo = clubRepRepo;
        _privilegeService = privilegeService;
    }

    public async Task<ClubRepRegistrationResponse> RegisterAsync(ClubRepRegistrationRequest request)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.ClubName) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return new ClubRepRegistrationResponse { Success = false, ClubId = null, UserId = null, Message = "Club name, username, and password are required" };
        }

        // Check if user already exists (by username or email)
        var existingUser = await _userManager.FindByNameAsync(request.Username);

        if (existingUser != null)
        {
            // User exists - validate privilege separation policy
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

            // Verify password for existing user
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

        // Check for similar existing clubs (fuzzy match) - search ALL states to find duplicates
        var similarClubs = await SearchClubsAsync(request.ClubName, null);

        // Return similar clubs for user to review - DON'T block registration
        // Frontend will present matches and ask: "Is this your club?"
        // User can choose to:
        //   A) Attach to existing club (create ClubRep record with existing ClubId)
        //   B) Create new club (proceed with new Clubs + ClubRep records)

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        ApplicationUser user;
        bool isNewUser = existingUser == null;

        if (isNewUser)
        {
            // Create new AspNetUser for the club rep
            user = new ApplicationUser
            {
                UserName = request.Username,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Cellphone = request.Cellphone,
                Phone = request.Cellphone,
                StreetAddress = request.StreetAddress,
                City = request.City,
                State = request.State,
                PostalCode = request.PostalCode,
                Modified = DateTime.UtcNow
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
            // Use existing validated user
            user = existingUser;
        }

        // Create Clubs record
        var club = new Domain.Entities.Clubs
        {
            ClubName = request.ClubName,
            LebUserId = user.Id, // Primary owner/contact
            Modified = DateTime.UtcNow
        };
        _clubRepo.Add(club);
        await _clubRepo.SaveChangesAsync();

        // Create ClubReps entry linking user to club
        var clubRep = new ClubReps
        {
            ClubId = club.ClubId,
            ClubRepUserId = user.Id
        };
        _clubRepRepo.Add(clubRep);
        await _clubRepRepo.SaveChangesAsync();

        scope.Complete();
        return new ClubRepRegistrationResponse { Success = true, ClubId = club.ClubId, UserId = user.Id, Message = null, SimilarClubs = similarClubs.Any() ? similarClubs : null };
    }

    public async Task<AddClubResponse> AddClubAsync(AddClubRequest request, string userId)
    {
        // Check for similar existing clubs (fuzzy match) - search ALL states
        var similarClubs = await SearchClubsAsync(request.ClubName, null);

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

            // Check if user already has access to this club
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

            // Create ClubRep association with existing club
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

        // User wants to create new club - get user info for club address
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

        // Create new club with user's address info
        var club = new TSIC.Domain.Entities.Clubs
        {
            ClubName = request.ClubName,
            LebUserId = user.Id
        };
        _clubRepo.Add(club);
        await _clubRepo.SaveChangesAsync();

        // Create ClubRep association
        var newClubRep = new TSIC.Domain.Entities.ClubReps
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
            SimilarClubs = similarClubs.Any() ? similarClubs : null
        };
    }

    public async Task<List<ClubSearchResult>> SearchClubsAsync(string query, string? state)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
        {
            return new List<ClubSearchResult>();
        }

        // Business logic: normalize search query
        var normalized = ClubNameMatcher.NormalizeClubName(query);

        // Data access: query clubs via repository (search all states to find duplicates)
        var clubs = await _clubRepo.GetSearchCandidatesAsync();

        // Business logic: calculate similarity scores using Application layer
        var results = clubs
            .Select(c => new ClubSearchResult
            {
                ClubId = c.ClubId,
                ClubName = c.ClubName,
                State = c.State,
                TeamCount = c.TeamCount,
                MatchScore = ClubNameMatcher.CalculateSimilarity(normalized, ClubNameMatcher.NormalizeClubName(c.ClubName))
            })
            .Where(r => r.MatchScore >= 60) // Only return 60%+ matches
            .OrderByDescending(r => r.MatchScore)
            .Take(5)
            .ToList();

        return results;
    }

}


