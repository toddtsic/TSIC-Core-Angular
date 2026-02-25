using Microsoft.AspNetCore.Identity;
using TSIC.Contracts.Dtos.Referees;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Referees;

/// <summary>
/// Service for referee assignment operations: search, assign, copy, import, seed, and calendar.
/// </summary>
public sealed class RefAssignmentService : IRefAssignmentService
{
    private readonly IRefAssignmentRepository _refRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly UserManager<ApplicationUser> _userManager;

    public RefAssignmentService(
        IRefAssignmentRepository refRepo,
        IRegistrationRepository registrationRepo,
        UserManager<ApplicationUser> userManager)
    {
        _refRepo = refRepo;
        _registrationRepo = registrationRepo;
        _userManager = userManager;
    }

    // ── Queries (delegate straight to repo) ──

    public Task<List<RefereeSummaryDto>> GetRefereesAsync(Guid jobId, CancellationToken ct = default)
        => _refRepo.GetRefereesForJobAsync(jobId, ct);

    public Task<RefScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default)
        => _refRepo.GetRefScheduleFilterOptionsAsync(jobId, ct);

    public Task<List<RefScheduleGameDto>> SearchScheduleAsync(Guid jobId, RefScheduleSearchRequest request, CancellationToken ct = default)
        => _refRepo.SearchScheduleAsync(jobId, request, ct);

    public Task<List<GameRefAssignmentDto>> GetAllAssignmentsAsync(Guid jobId, CancellationToken ct = default)
        => _refRepo.GetAllAssignmentsForJobAsync(jobId, ct);

    public Task<List<RefGameDetailsDto>> GetGameRefDetailsAsync(int gid, Guid jobId, CancellationToken ct = default)
        => _refRepo.GetGameRefDetailsAsync(gid, jobId, ct);

    public Task<List<RefereeCalendarEventDto>> GetCalendarEventsAsync(Guid jobId, CancellationToken ct = default)
        => _refRepo.GetCalendarEventsAsync(jobId, ct);

    // ── Assign Refs ──

    public async Task AssignRefsToGameAsync(AssignRefsRequest request, string auditUserId, CancellationToken ct = default)
    {
        await _refRepo.ReplaceAssignmentsForGameAsync(request.Gid, request.RefRegistrationIds, auditUserId, ct);
    }

    // ── Copy Refs ──

    public async Task<List<int>> CopyGameRefsAsync(Guid jobId, CopyGameRefsRequest request, string auditUserId, CancellationToken ct = default)
    {
        // Get source game's assigned ref IDs
        var sourceAssignments = await _refRepo.GetAssignmentsForGameAsync(request.Gid, ct);
        var refIds = sourceAssignments
            .Where(a => a.RefRegistrationId != null)
            .Select(a => a.RefRegistrationId!.Value)
            .ToList();

        if (refIds.Count == 0)
            return [];

        // Find source game's field + date via a targeted search
        var allGames = await _refRepo.SearchScheduleAsync(jobId, new RefScheduleSearchRequest(), ct);
        var sourceGame = allGames.FirstOrDefault(g => g.Gid == request.Gid);
        if (sourceGame?.FieldId == null)
            return [];

        // Get all games on the same field for the same date, ordered by time
        var gamesOnField = await _refRepo.GetGamesOnFieldForDateAsync(
            sourceGame.FieldId.Value, sourceGame.GameDate, jobId, ct);

        var sourceIndex = gamesOnField.FindIndex(g => g.Gid == request.Gid);
        if (sourceIndex < 0)
            return [];

        // Walk in the requested direction, applying skip interval
        var affectedGids = new List<int>();
        var step = request.SkipInterval + 1;
        var collected = 0;

        if (request.CopyDown)
        {
            for (var i = sourceIndex + step; i < gamesOnField.Count && collected < request.NumberTimeslots; i += step)
            {
                affectedGids.Add(gamesOnField[i].Gid);
                collected++;
            }
        }
        else
        {
            for (var i = sourceIndex - step; i >= 0 && collected < request.NumberTimeslots; i -= step)
            {
                affectedGids.Add(gamesOnField[i].Gid);
                collected++;
            }
        }

        // Apply assignments to each target game (sequential — DbContext not thread-safe)
        foreach (var targetGid in affectedGids)
        {
            await _refRepo.ReplaceAssignmentsForGameAsync(targetGid, refIds, auditUserId, ct);
        }

        return affectedGids;
    }

    // ── Import Refs from CSV ──

    public async Task<ImportRefereesResult> ImportRefereesAsync(Guid jobId, Stream csvStream, string auditUserId, CancellationToken ct = default)
    {
        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();

        using var reader = new StreamReader(csvStream);
        var lineNumber = 0;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Skip header row
            if (lineNumber == 1 && line.Contains("FirstName", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 2)
            {
                errors.Add($"Line {lineNumber}: insufficient columns (need at least FirstName, LastName)");
                continue;
            }

            var firstName = parts[0].Trim().Trim('"');
            var lastName = parts[1].Trim().Trim('"');
            var email = parts.Length > 2 ? parts[2].Trim().Trim('"') : "";
            var cellPhone = parts.Length > 3 ? parts[3].Trim().Trim('"') : null;
            var street = parts.Length > 4 ? parts[4].Trim().Trim('"') : null;
            var city = parts.Length > 5 ? parts[5].Trim().Trim('"') : null;
            var state = parts.Length > 6 ? parts[6].Trim().Trim('"') : null;
            var zip = parts.Length > 7 ? parts[7].Trim().Trim('"') : null;
            var dobStr = parts.Length > 8 ? parts[8].Trim().Trim('"') : null;
            var gender = parts.Length > 9 ? parts[9].Trim().Trim('"') : null;
            var certNumber = parts.Length > 10 ? parts[10].Trim().Trim('"') : null;
            var certExpiryStr = parts.Length > 11 ? parts[11].Trim().Trim('"') : null;

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                errors.Add($"Line {lineNumber}: FirstName and LastName are required");
                continue;
            }

            // Generate username
            var username = $"Ref-{firstName[0]}{lastName}".Replace(" ", "");

            // Skip duplicate usernames
            var existingUser = await _userManager.FindByNameAsync(username);
            if (existingUser != null)
            {
                skipped++;
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = username,
                Email = !string.IsNullOrWhiteSpace(email) ? email : $"{username}@ref.local",
                FirstName = firstName,
                LastName = lastName,
                Gender = !string.IsNullOrWhiteSpace(gender) ? gender : "U",
                Cellphone = cellPhone,
                StreetAddress = street,
                City = city,
                State = state,
                PostalCode = zip,
                Dob = DateTime.TryParse(dobStr, out var dob) ? dob : new DateTime(1980, 1, 1),
                LebUserId = auditUserId,
                Modified = DateTime.UtcNow
            };

            var createResult = await _userManager.CreateAsync(user, username);
            if (!createResult.Succeeded)
            {
                errors.Add($"Line {lineNumber}: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
                continue;
            }

            _registrationRepo.Add(new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                UserId = user.Id,
                RoleId = RoleConstants.Referee,
                JobId = jobId,
                BActive = true,
                RegistrationTs = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                LebUserId = auditUserId,
                SportAssnId = certNumber,
                SportAssnIdexpDate = DateTime.TryParse(certExpiryStr, out var certExpiry) ? certExpiry : null
            });
            await _registrationRepo.SaveChangesAsync(ct);
            imported++;
        }

        return new ImportRefereesResult
        {
            Imported = imported,
            Skipped = skipped,
            Errors = errors
        };
    }

    // ── Seed Test Refs ──

    public async Task<List<RefereeSummaryDto>> SeedTestRefereesAsync(Guid jobId, int count, string auditUserId, CancellationToken ct = default)
    {
        for (var i = 1; i <= count; i++)
        {
            var paddedNum = i.ToString("D3");
            var username = $"TestRef-{paddedNum}";

            if (await _userManager.FindByNameAsync(username) != null)
                continue;

            var user = new ApplicationUser
            {
                UserName = username,
                Email = $"{username}@test.local",
                FirstName = "Test",
                LastName = $"Referee {paddedNum}",
                Gender = "U",
                Dob = new DateTime(1990, 1, 1),
                LebUserId = auditUserId,
                Modified = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, username);
            if (!result.Succeeded)
                continue;

            _registrationRepo.Add(new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                UserId = user.Id,
                RoleId = RoleConstants.Referee,
                JobId = jobId,
                BActive = true,
                RegistrationTs = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                LebUserId = auditUserId
            });
            await _registrationRepo.SaveChangesAsync(ct);
        }

        return await _refRepo.GetRefereesForJobAsync(jobId, ct);
    }

    // ── Purge All ──

    public async Task DeleteAllAsync(Guid jobId, CancellationToken ct = default)
    {
        await _refRepo.DeleteAllAssignmentsForJobAsync(jobId, ct);
        await _refRepo.DeleteAllRefereeRegistrationsForJobAsync(jobId, ct);
    }

}
