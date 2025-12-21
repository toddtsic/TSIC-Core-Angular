using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Transactions;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Application.Services.Users;
using TSIC.Application.Services.Clubs;
using TSIC.Application.Services.Shared;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services.Clubs;

public sealed class ClubService : IClubService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SqlDbContext _db;
    private readonly IUserPrivilegeLevelService _privilegeService;

    public ClubService(
        UserManager<ApplicationUser> userManager,
        SqlDbContext db,
        IUserPrivilegeLevelService privilegeService)
    {
        _userManager = userManager;
        _db = db;
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
        var existingUser = await _userManager.FindByNameAsync(request.Username)
            ?? await _userManager.FindByEmailAsync(request.Email);

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

        // Check for similar existing clubs (fuzzy match)
        var similarClubs = await SearchClubsAsync(request.ClubName, request.State);

        // If exact match found (90%+ similarity), warn user
        var exactMatch = similarClubs.FirstOrDefault(c => c.MatchScore >= 90);
        if (exactMatch != null)
        {
            return new ClubRepRegistrationResponse
            {
                Success = false,
                ClubId = null,
                UserId = null,
                Message = $"A club with a very similar name already exists: '{exactMatch.ClubName}'. If this is a duplicate registration, your teams may be dropped. Please verify this is a NEW club or login to the existing club instead.",
                SimilarClubs = similarClubs
            };
        }

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
        _db.Clubs.Add(club);
        await _db.SaveChangesAsync();

        // Create ClubReps entry linking user to club
        var clubRep = new ClubReps
        {
            ClubId = club.ClubId,
            ClubRepUserId = user.Id
        };
        _db.ClubReps.Add(clubRep);
        await _db.SaveChangesAsync();

        scope.Complete();
        return new ClubRepRegistrationResponse { Success = true, ClubId = club.ClubId, UserId = user.Id, Message = null, SimilarClubs = similarClubs.Any() ? similarClubs : null };
    }

    public async Task<List<ClubSearchResult>> SearchClubsAsync(string query, string? state)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
        {
            return new List<ClubSearchResult>();
        }

        // Business logic: normalize search query
        var normalized = ClubNameMatcher.NormalizeClubName(query);

        // Data access: query all clubs (optionally filtered by state)
        var clubsQuery = _db.Clubs
            .Where(c => state == null || c.LebUser!.State == state);

        var clubs = await clubsQuery
            .Select(c => new
            {
                c.ClubId,
                c.ClubName,
                State = c.LebUser!.State,
                TeamCount = c.ClubTeams.Count
            })
            .ToListAsync();

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


