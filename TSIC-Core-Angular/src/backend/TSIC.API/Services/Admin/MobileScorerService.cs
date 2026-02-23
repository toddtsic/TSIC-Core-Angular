using Microsoft.AspNetCore.Identity;
using TSIC.Contracts.Dtos.Scoring;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for managing mobile scorer accounts.
/// </summary>
public class MobileScorerService : IMobileScorerService
{
    private readonly IMobileScorerRepository _repo;
    private readonly UserManager<ApplicationUser> _userManager;

    public MobileScorerService(
        IMobileScorerRepository repo,
        UserManager<ApplicationUser> userManager)
    {
        _repo = repo;
        _userManager = userManager;
    }

    public async Task<List<MobileScorerDto>> GetScorersAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _repo.GetScorersForJobAsync(jobId, ct);
    }

    public async Task<MobileScorerDto> CreateScorerAsync(
        Guid jobId,
        CreateMobileScorerRequest request,
        string currentUserId,
        CancellationToken ct = default)
    {
        var username = request.Username.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.");
        if (username.Length < 6)
            throw new ArgumentException("Username must be at least 6 characters.");
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required.");
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required.");

        // Create ASP.NET Identity user (password = username convention)
        var user = new ApplicationUser
        {
            UserName = username,
            FirstName = firstName,
            LastName = lastName,
            Email = request.Email?.Trim(),
            Cellphone = request.Cellphone?.Trim(),
            Gender = "U",
            Dob = new DateTime(1980, 1, 1),
            LebUserId = currentUserId,
            Modified = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, password: username);
        if (!result.Succeeded)
        {
            var errorMessage = result.Errors.First().Description;
            throw new InvalidOperationException(errorMessage);
        }

        // Create Scorer registration for this job
        var registration = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            UserId = user.Id,
            JobId = jobId,
            RoleId = RoleConstants.Scorer,
            BActive = true,
            AssignedTeamId = null,
            FamilyUserId = null,
            RegistrationFormName = null,
            RegistrationTs = DateTime.UtcNow,
            LebUserId = currentUserId,
            Modified = DateTime.UtcNow
        };

        _repo.AddRegistration(registration);
        await _repo.SaveChangesAsync(ct);

        return new MobileScorerDto
        {
            RegistrationId = registration.RegistrationId,
            Username = user.UserName ?? "",
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Cellphone = user.Cellphone,
            BActive = true
        };
    }

    public async Task UpdateScorerAsync(
        Guid registrationId,
        UpdateMobileScorerRequest request,
        string currentUserId,
        CancellationToken ct = default)
    {
        var registration = await _repo.GetScorerRegistrationAsync(registrationId, ct)
            ?? throw new KeyNotFoundException($"Scorer registration {registrationId} not found.");

        // Update registration active status
        registration.BActive = request.BActive;
        registration.LebUserId = currentUserId;
        registration.Modified = DateTime.UtcNow;

        // Update user contact fields
        var user = await _userManager.FindByIdAsync(registration.UserId!)
            ?? throw new KeyNotFoundException("Scorer user account not found.");

        user.Email = request.Email?.Trim();
        user.Cellphone = request.Cellphone?.Trim();
        user.Modified = DateTime.UtcNow;

        await _userManager.UpdateAsync(user);
        await _repo.SaveChangesAsync(ct);
    }

    public async Task DeleteScorerAsync(Guid registrationId, CancellationToken ct = default)
    {
        var registration = await _repo.GetScorerRegistrationAsync(registrationId, ct)
            ?? throw new KeyNotFoundException($"Scorer registration {registrationId} not found.");

        var userId = registration.UserId!;

        // Remove the registration
        _repo.RemoveRegistration(registration);
        await _repo.SaveChangesAsync(ct);

        // If user has no other registrations, delete the user account too
        var remainingCount = await _repo.GetUserRegistrationCountAsync(userId, ct);
        if (remainingCount == 0)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
        }
    }
}
