using TSIC.Contracts.Dtos.AgeRange;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

public class AgeRangeService : IAgeRangeService
{
    private readonly IAgeRangeRepository _ageRangeRepository;

    public AgeRangeService(IAgeRangeRepository ageRangeRepository)
    {
        _ageRangeRepository = ageRangeRepository;
    }

    public async Task<List<AgeRangeDto>> GetAllForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _ageRangeRepository.GetAllForJobAsync(jobId, cancellationToken);
    }

    public async Task<AgeRangeDto> CreateAsync(
        Guid jobId,
        string userId,
        CreateAgeRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RangeLeft > request.RangeRight)
        {
            throw new InvalidOperationException("Start date must be on or before end date.");
        }

        var nameExists = await _ageRangeRepository.ExistsWithNameAsync(
            jobId, request.RangeName, null, cancellationToken);
        if (nameExists)
        {
            throw new InvalidOperationException("Range name already exists.");
        }

        var (overlaps, overlappingName) = await _ageRangeRepository.HasOverlapAsync(
            jobId, request.RangeLeft, request.RangeRight, null, cancellationToken);
        if (overlaps)
        {
            throw new InvalidOperationException(
                $"Invalid range: your range overlaps with \"{overlappingName}\".");
        }

        var entity = new JobAgeRanges
        {
            JobId = jobId,
            RangeName = request.RangeName.Trim(),
            RangeLeft = request.RangeLeft,
            RangeRight = request.RangeRight,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _ageRangeRepository.Add(entity);
        await _ageRangeRepository.SaveChangesAsync(cancellationToken);

        return new AgeRangeDto
        {
            AgeRangeId = entity.AgeRangeId,
            RangeName = entity.RangeName ?? string.Empty,
            RangeLeft = entity.RangeLeft,
            RangeRight = entity.RangeRight,
            Modified = entity.Modified,
            ModifiedByUsername = null
        };
    }

    public async Task<AgeRangeDto> UpdateAsync(
        int ageRangeId,
        Guid jobId,
        string userId,
        UpdateAgeRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _ageRangeRepository.GetByIdAsync(ageRangeId, cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException($"Age range with ID {ageRangeId} not found.");
        }

        if (entity.JobId != jobId)
        {
            throw new InvalidOperationException("Age range does not belong to the current job.");
        }

        if (request.RangeLeft > request.RangeRight)
        {
            throw new InvalidOperationException("Start date must be on or before end date.");
        }

        var nameExists = await _ageRangeRepository.ExistsWithNameAsync(
            jobId, request.RangeName, ageRangeId, cancellationToken);
        if (nameExists)
        {
            throw new InvalidOperationException("Range name already exists.");
        }

        var (overlaps, overlappingName) = await _ageRangeRepository.HasOverlapAsync(
            jobId, request.RangeLeft, request.RangeRight, ageRangeId, cancellationToken);
        if (overlaps)
        {
            throw new InvalidOperationException(
                $"Invalid range: your range overlaps with \"{overlappingName}\".");
        }

        entity.RangeName = request.RangeName.Trim();
        entity.RangeLeft = request.RangeLeft;
        entity.RangeRight = request.RangeRight;
        entity.LebUserId = userId;
        entity.Modified = DateTime.UtcNow;

        await _ageRangeRepository.SaveChangesAsync(cancellationToken);

        return new AgeRangeDto
        {
            AgeRangeId = entity.AgeRangeId,
            RangeName = entity.RangeName ?? string.Empty,
            RangeLeft = entity.RangeLeft,
            RangeRight = entity.RangeRight,
            Modified = entity.Modified,
            ModifiedByUsername = null
        };
    }

    public async Task<bool> DeleteAsync(
        int ageRangeId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _ageRangeRepository.GetByIdAsync(ageRangeId, cancellationToken);
        if (entity == null)
        {
            return false;
        }

        if (entity.JobId != jobId)
        {
            throw new InvalidOperationException("Age range does not belong to the current job.");
        }

        _ageRangeRepository.Remove(entity);
        await _ageRangeRepository.SaveChangesAsync(cancellationToken);
        return true;
    }
}
