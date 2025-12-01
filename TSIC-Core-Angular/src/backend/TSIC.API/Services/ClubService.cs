using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Transactions;
using TSIC.API.Dtos;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public sealed class ClubService : IClubService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SqlDbContext _db;

    public ClubService(UserManager<ApplicationUser> userManager, SqlDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<ClubRegistrationResponse> RegisterAsync(ClubRegistrationRequest request)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.ClubName) || 
            string.IsNullOrWhiteSpace(request.Username) || 
            string.IsNullOrWhiteSpace(request.Password))
        {
            return new ClubRegistrationResponse(false, null, null, "Club name, username, and password are required");
        }

        // Check for similar existing clubs (fuzzy match)
        var similarClubs = await SearchClubsAsync(request.ClubName, request.State);
        
        // If exact match found (90%+ similarity), warn user
        var exactMatch = similarClubs.FirstOrDefault(c => c.MatchScore >= 90);
        if (exactMatch != null)
        {
            return new ClubRegistrationResponse(
                false, 
                null, 
                null, 
                $"A club with a very similar name already exists: '{exactMatch.ClubName}'. If this is a duplicate registration, your teams may be dropped. Please verify this is a NEW club or login to the existing club instead.",
                similarClubs
            );
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        // Create AspNetUser for the club rep
        var user = new ApplicationUser
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
            return new ClubRegistrationResponse(false, null, null, msg);
        }

        // Create Clubs record
        var club = new Clubs
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
        return new ClubRegistrationResponse(true, club.ClubId, user.Id, null, similarClubs.Any() ? similarClubs : null);
    }

    public async Task<List<ClubSearchResult>> SearchClubsAsync(string query, string? state)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
        {
            return new List<ClubSearchResult>();
        }

        var normalized = NormalizeClubName(query);

        // Query all clubs (optionally filtered by state)
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

        // Calculate similarity scores
        var results = clubs
            .Select(c => new ClubSearchResult(
                c.ClubId,
                c.ClubName,
                c.State,
                c.TeamCount,
                CalculateSimilarity(normalized, NormalizeClubName(c.ClubName))
            ))
            .Where(r => r.MatchScore >= 60) // Only return 60%+ matches
            .OrderByDescending(r => r.MatchScore)
            .Take(5)
            .ToList();

        return results;
    }

    /// <summary>
    /// Normalize club name for fuzzy matching:
    /// - Lowercase
    /// - Remove punctuation
    /// - Expand common abbreviations
    /// </summary>
    private static string NormalizeClubName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var normalized = name.ToLowerInvariant();
        
        // Common lacrosse/sports abbreviations
        normalized = normalized.Replace("lax", "lacrosse")
                               .Replace("lc", "lacrosse club")
                               .Replace("fc", "football club")
                               .Replace("sc", "soccer club")
                               .Replace("yc", "youth club");

        // Remove punctuation and extra spaces
        normalized = new string(normalized.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
        normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return normalized;
    }

    /// <summary>
    /// Calculate Levenshtein distance-based similarity (0-100)
    /// </summary>
    private static int CalculateSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 100;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;

        var distance = LevenshteinDistance(s1, s2);
        var maxLen = Math.Max(s1.Length, s2.Length);
        var similarity = (1.0 - (double)distance / maxLen) * 100;
        return (int)Math.Round(similarity);
    }

    /// <summary>
    /// Levenshtein distance algorithm
    /// </summary>
    private static int LevenshteinDistance(string s1, string s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;
        var matrix = new int[len1 + 1, len2 + 1];

        for (int i = 0; i <= len1; i++) matrix[i, 0] = i;
        for (int j = 0; j <= len2; j++) matrix[0, j] = j;

        for (int i = 1; i <= len1; i++)
        {
            for (int j = 1; j <= len2; j++)
            {
                int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost
                );
            }
        }

        return matrix[len1, len2];
    }
}
