using Microsoft.AspNetCore.Identity;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Account;

/// <summary>
/// Single owner of self-service ApplicationUser profile read/write. Lifted out of
/// ClubService so the adult-registration wizard (and any future self-service
/// surface) shares the exact same logic. ClubService now delegates here.
/// </summary>
public sealed class UserProfileService : IUserProfileService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserProfileService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<UserProfileDto?> GetSelfProfileAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return null;
        }

        return new UserProfileDto
        {
            FirstName = user.FirstName ?? string.Empty,
            LastName = user.LastName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Cellphone = user.Cellphone ?? string.Empty,
            StreetAddress = user.StreetAddress ?? string.Empty,
            City = user.City ?? string.Empty,
            State = user.State ?? string.Empty,
            PostalCode = user.PostalCode ?? string.Empty
        };
    }

    public async Task<bool> UpdateSelfProfileAsync(string userId, UserProfileUpdateRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return false;
        }

        // SetEmailAsync keeps the normalized email index in sync. Only call when changed.
        if (!string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailResult = await _userManager.SetEmailAsync(user, request.Email);
            if (!emailResult.Succeeded)
            {
                return false;
            }
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Cellphone = request.Cellphone;
        user.Phone = request.Cellphone;
        user.StreetAddress = request.StreetAddress;
        user.City = request.City;
        user.State = request.State;
        user.PostalCode = request.PostalCode;
        user.Modified = DateTime.Now;

        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded;
    }

    public async Task<bool> IsUsernameAvailableAsync(string username)
    {
        var candidate = username?.Trim() ?? string.Empty;
        if (candidate.Length == 0)
        {
            return false;
        }

        // FindByNameAsync applies the same ILookupNormalizer that CreateAsync uses,
        // so "taken" here is exactly what account creation would reject.
        var existing = await _userManager.FindByNameAsync(candidate);
        return existing == null;
    }
}
