using TSIC.Contracts.Dtos.DiscountCode;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Application.Services.DiscountCode;

/// <summary>
/// Service implementation for managing discount codes.
/// </summary>
public class DiscountCodeService : IDiscountCodeService
{
    private readonly IJobDiscountCodeRepository _repository;

    public DiscountCodeService(IJobDiscountCodeRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<DiscountCodeDto>> GetDiscountCodesAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var codes = await _repository.GetAllByJobIdAsync(jobId, cancellationToken);
        var now = DateTime.UtcNow;

        var dtos = new List<DiscountCodeDto>();
        foreach (var code in codes)
        {
            var usageCount = await _repository.GetUsageCountAsync(code.Ai, cancellationToken);
            dtos.Add(MapToDto(code, usageCount, now));
        }

        return dtos;
    }

    public async Task<DiscountCodeDto> AddDiscountCodeAsync(
        Guid jobId,
        string userId,
        AddDiscountCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate code doesn't already exist
        if (await _repository.CodeExistsAsync(jobId, request.CodeName, cancellationToken))
        {
            throw new InvalidOperationException($"Discount code '{request.CodeName}' already exists for this job.");
        }

        // Validate date range
        if (request.EndDate <= request.StartDate)
        {
            throw new InvalidOperationException("End date must be after start date.");
        }

        var code = new JobDiscountCodes
        {
            JobId = jobId,
            CodeName = request.CodeName.Trim(),
            BAsPercent = request.DiscountType == "Percentage",
            CodeAmount = request.Amount,
            Active = true,
            CodeStartDate = request.StartDate,
            CodeEndDate = request.EndDate,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _repository.Add(code);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapToDto(code, 0, DateTime.UtcNow);
    }

    public async Task<List<DiscountCodeDto>> BulkAddDiscountCodesAsync(
        Guid jobId,
        string userId,
        BulkAddDiscountCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate date range
        if (request.EndDate <= request.StartDate)
        {
            throw new InvalidOperationException("End date must be after start date.");
        }

        // Generate all code names first
        var codeNames = new List<string>();
        for (int i = 0; i < request.Count; i++)
        {
            var number = (request.StartNumber + i).ToString().PadLeft(3, '0');
            var codeName = $"{request.Prefix}{number}{request.Suffix}".Trim();
            codeNames.Add(codeName);
        }

        // Check if any already exist (all-or-nothing)
        foreach (var codeName in codeNames)
        {
            if (await _repository.CodeExistsAsync(jobId, codeName, cancellationToken))
            {
                throw new InvalidOperationException($"Discount code '{codeName}' already exists. Bulk generation cancelled (all-or-nothing).");
            }
        }

        // Create all codes
        var codes = new List<JobDiscountCodes>();
        var now = DateTime.UtcNow;
        var isPercent = request.DiscountType == "Percentage";

        foreach (var codeName in codeNames)
        {
            var code = new JobDiscountCodes
            {
                JobId = jobId,
                CodeName = codeName,
                BAsPercent = isPercent,
                CodeAmount = request.Amount,
                Active = true,
                CodeStartDate = request.StartDate,
                CodeEndDate = request.EndDate,
                LebUserId = userId,
                Modified = now
            };
            _repository.Add(code);
            codes.Add(code);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return codes.Select(c => MapToDto(c, 0, now)).ToList();
    }

    public async Task<DiscountCodeDto> UpdateDiscountCodeAsync(
        int ai,
        UpdateDiscountCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = await _repository.GetByIdAsync(ai, cancellationToken);
        if (code == null)
        {
            throw new InvalidOperationException($"Discount code with ID {ai} not found.");
        }

        // Validate date range
        if (request.EndDate <= request.StartDate)
        {
            throw new InvalidOperationException("End date must be after start date.");
        }

        // Update fields (CodeName and UsageCount are NOT editable)
        code.BAsPercent = request.DiscountType == "Percentage";
        code.CodeAmount = request.Amount;
        code.CodeStartDate = request.StartDate;
        code.CodeEndDate = request.EndDate;
        code.Active = request.IsActive;
        code.Modified = DateTime.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        var usageCount = await _repository.GetUsageCountAsync(ai, cancellationToken);
        return MapToDto(code, usageCount, DateTime.UtcNow);
    }

    public async Task<bool> DeleteDiscountCodeAsync(int ai, CancellationToken cancellationToken = default)
    {
        var code = await _repository.GetByIdAsync(ai, cancellationToken);
        if (code == null)
        {
            return false;
        }

        // Prevent deletion if code has been used
        var usageCount = await _repository.GetUsageCountAsync(ai, cancellationToken);
        if (usageCount > 0)
        {
            throw new InvalidOperationException($"Cannot delete discount code '{code.CodeName}' because it has been used {usageCount} time(s).");
        }

        _repository.Remove(code);
        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<int> BatchUpdateStatusAsync(
        Guid jobId,
        List<int> codeIds,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var updateCount = 0;

        foreach (var codeId in codeIds)
        {
            var code = await _repository.GetByIdAsync(codeId, cancellationToken);
            if (code != null && code.JobId == jobId) // Security: verify job ownership
            {
                code.Active = isActive;
                code.Modified = DateTime.UtcNow;
                updateCount++;
            }
        }

        if (updateCount > 0)
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }

        return updateCount;
    }

    public async Task<bool> CheckCodeExistsAsync(
        Guid jobId,
        string codeName,
        CancellationToken cancellationToken = default)
    {
        return await _repository.CodeExistsAsync(jobId, codeName, cancellationToken);
    }

    // === HELPER METHODS ===

    private static DiscountCodeDto MapToDto(JobDiscountCodes code, int usageCount, DateTime now)
    {
        return new DiscountCodeDto
        {
            Ai = code.Ai,
            CodeName = code.CodeName,
            DiscountType = code.BAsPercent ? "Percentage" : "DollarAmount",
            Amount = code.CodeAmount ?? 0,
            UsageCount = usageCount,
            StartDate = code.CodeStartDate,
            EndDate = code.CodeEndDate,
            IsActive = code.Active,
            IsExpired = code.CodeEndDate < now,
            Modified = code.Modified
        };
    }
}
