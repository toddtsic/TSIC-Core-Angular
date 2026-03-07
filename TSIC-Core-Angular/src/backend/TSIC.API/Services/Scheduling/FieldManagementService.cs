using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Service for the Manage Fields scheduling tool.
/// Orchestrates field CRUD and league-season assignment via repositories.
/// </summary>
public sealed class FieldManagementService : IFieldManagementService
{
    private readonly IFieldRepository _fieldRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly ILogger<FieldManagementService> _logger;

    public FieldManagementService(
        IFieldRepository fieldRepo,
        IJobRepository jobRepo,
        IScheduleRepository scheduleRepo,
        ISchedulingContextResolver contextResolver,
        ILogger<FieldManagementService> logger)
    {
        _fieldRepo = fieldRepo;
        _jobRepo = jobRepo;
        _scheduleRepo = scheduleRepo;
        _contextResolver = contextResolver;
        _logger = logger;
    }

    public async Task<FieldManagementResponse> GetFieldManagementDataAsync(
        Guid jobId, string userRole, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        var isSuperUser = string.Equals(userRole, "SuperUser", StringComparison.OrdinalIgnoreCase);

        // For Directors, get all jobs owned by the same customer
        var directorJobIds = isSuperUser
            ? []
            : await _jobRepo.GetCustomerJobIdsAsync(jobId, ct);

        var availableFields = await _fieldRepo.GetAvailableFieldsAsync(
            leagueId, season, directorJobIds, isSuperUser, ct);

        var assignedRecords = await _fieldRepo.GetLeagueSeasonFieldsAsync(leagueId, season, ct);

        // Enrich assigned fields with scheduled game counts
        var assignedFieldIds = assignedRecords.Select(f => f.FieldId).ToList();
        var gameCounts = assignedFieldIds.Count > 0
            ? await _scheduleRepo.GetGameCountsByFieldIdsAsync(jobId, assignedFieldIds, ct)
            : new Dictionary<Guid, int>();

        var enrichedAssigned = assignedRecords.Select(f => f with
        {
            ScheduledGameCount = gameCounts.GetValueOrDefault(f.FieldId)
        }).ToList();

        return new FieldManagementResponse
        {
            AvailableFields = availableFields.Select(f => new FieldDto
            {
                FieldId = f.FieldId,
                FName = f.FName ?? "",
                Address = f.Address,
                City = f.City,
                State = f.State,
                Zip = f.Zip,
                Directions = f.Directions,
                Latitude = f.Latitude,
                Longitude = f.Longitude
            }).ToList(),
            AssignedFields = enrichedAssigned
        };
    }

    public async Task<FieldDto> CreateFieldAsync(
        Guid jobId, string userId, CreateFieldRequest request, CancellationToken ct = default)
    {
        var field = new Fields
        {
            FieldId = Guid.NewGuid(),
            FName = request.FName,
            Address = request.Address,
            City = request.City,
            State = request.State,
            Zip = request.Zip,
            Directions = request.Directions,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            LebUserId = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty,
            Modified = DateTime.UtcNow
        };

        _fieldRepo.Add(field);
        await _fieldRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Field {FieldId} '{FName}' created by {UserId}", field.FieldId, field.FName, userId);

        return new FieldDto
        {
            FieldId = field.FieldId,
            FName = field.FName ?? "",
            Address = field.Address,
            City = field.City,
            State = field.State,
            Zip = field.Zip,
            Directions = field.Directions,
            Latitude = field.Latitude,
            Longitude = field.Longitude
        };
    }

    public async Task UpdateFieldAsync(
        string userId, UpdateFieldRequest request, CancellationToken ct = default)
    {
        var field = await _fieldRepo.GetFieldTrackedAsync(request.FieldId, ct)
            ?? throw new KeyNotFoundException($"Field {request.FieldId} not found.");

        field.FName = request.FName;
        field.Address = request.Address;
        field.City = request.City;
        field.State = request.State;
        field.Zip = request.Zip;
        field.Directions = request.Directions;
        field.Latitude = request.Latitude;
        field.Longitude = request.Longitude;
        field.LebUserId = Guid.TryParse(userId, out var uid) ? uid : field.LebUserId;
        field.Modified = DateTime.UtcNow;

        await _fieldRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Field {FieldId} '{FName}' updated by {UserId}", field.FieldId, field.FName, userId);
    }

    public async Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken ct = default)
    {
        var isReferenced = await _fieldRepo.IsFieldReferencedAsync(fieldId, ct);
        if (isReferenced)
            return false;

        var field = await _fieldRepo.GetFieldTrackedAsync(fieldId, ct);
        if (field == null)
            return false;

        _fieldRepo.Remove(field);
        await _fieldRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Field {FieldId} deleted", fieldId);
        return true;
    }

    public async Task AssignFieldsAsync(
        Guid jobId, string userId, AssignFieldsRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);
        await _fieldRepo.AssignFieldsToLeagueSeasonAsync(leagueId, season, request.FieldIds, userId, ct);

        _logger.LogInformation("Assigned {Count} fields to league {LeagueId} season {Season}",
            request.FieldIds.Count, leagueId, season);
    }

    public async Task RemoveFieldsAsync(
        Guid jobId, RemoveFieldsRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        // Guard: block removal of fields that have scheduled games
        var gameCounts = await _scheduleRepo.GetGameCountsByFieldIdsAsync(jobId, request.FieldIds, ct);
        if (gameCounts.Count > 0)
        {
            // Resolve field names for a helpful error message
            var fieldNames = await _fieldRepo.GetFieldNamesByIdsAsync(request.FieldIds, ct);
            var details = gameCounts
                .Select(kv =>
                {
                    var name = fieldNames.GetValueOrDefault(kv.Key, kv.Key.ToString());
                    return $"{name} ({kv.Value} game{(kv.Value != 1 ? "s" : "")})";
                })
                .ToList();

            throw new InvalidOperationException(
                $"Cannot remove — games are scheduled on: {string.Join(", ", details)}. " +
                "Delete the games first, then remove the field.");
        }

        await _fieldRepo.RemoveFieldsFromLeagueSeasonAsync(leagueId, season, request.FieldIds, ct);

        _logger.LogInformation("Removed {Count} fields from league {LeagueId} season {Season}",
            request.FieldIds.Count, leagueId, season);
    }

    public async Task UpdateFieldPreferenceAsync(
        Guid flsId, int fieldPreference, CancellationToken ct = default)
    {
        await _fieldRepo.UpdateFieldPreferenceAsync(flsId, (byte)fieldPreference, ct);
    }
}
