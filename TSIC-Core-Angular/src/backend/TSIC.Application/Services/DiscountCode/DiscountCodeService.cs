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
        var rows = await _repository.GetAllByJobIdWithUsageAsync(jobId, cancellationToken);
        // Discount-code start/end dates are stored in local AZ time, not UTC.
        var now = DateTime.Now;

        return rows.Select(r => MapToDto(r.Code, r.UsageCount, now)).ToList();
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

        var isPercent = ParseDiscountType(request.DiscountType);
        ValidateAmount(request.Amount, isPercent);

        // Validate date range
        if (request.EndDate <= request.StartDate)
        {
            throw new InvalidOperationException("End date must be after start date.");
        }

        var code = new JobDiscountCodes
        {
            JobId = jobId,
            CodeName = request.CodeName.Trim(),
            BAsPercent = isPercent,
            CodeAmount = request.Amount,
            Active = true,
            CodeStartDate = request.StartDate,
            CodeEndDate = request.EndDate,
            LebUserId = userId,
            Modified = DateTime.Now
        };

        _repository.Add(code);
        await _repository.SaveChangesAsync(cancellationToken);

        return MapToDto(code, 0, DateTime.Now);
    }

    public async Task<List<DiscountCodeDto>> BulkAddDiscountCodesAsync(
        Guid jobId,
        string userId,
        BulkAddDiscountCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var isPercent = ParseDiscountType(request.DiscountType);
        ValidateAmount(request.Amount, isPercent);

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
        // Discount-code start/end dates are stored in local AZ time, not UTC.
        var now = DateTime.Now;

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
                Modified = DateTime.Now
            };
            _repository.Add(code);
            codes.Add(code);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return codes.Select(c => MapToDto(c, 0, now)).ToList();
    }

    public async Task<DiscountCodeDto> UpdateDiscountCodeAsync(
        Guid jobId,
        int ai,
        string userId,
        UpdateDiscountCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = await _repository.GetByIdAsync(ai, cancellationToken);
        if (code == null || code.JobId != jobId)
        {
            // Deliberately the same message either way: Ai is a guessable sequential int, so a
            // distinct "wrong job" error would turn this endpoint into an existence oracle for
            // other jobs' codes.
            throw new InvalidOperationException($"Discount code with ID {ai} not found.");
        }

        var isPercent = ParseDiscountType(request.DiscountType);
        ValidateAmount(request.Amount, isPercent);

        // Validate date range
        if (request.EndDate <= request.StartDate)
        {
            throw new InvalidOperationException("End date must be after start date.");
        }

        var usageCount = await _repository.GetUsageCountAsync(ai, cancellationToken);

        if (usageCount > 0)
        {
            // A redeemed code's row is the ONLY surviving record of what a registrant was given:
            // the redemption stacks the code's dollars into reg.FeeDiscount alongside early-bird
            // (PlayerRegistrationPaymentController), so the code's own contribution cannot be
            // recovered from the registration afterwards. Rewriting the terms here would erase
            // that record and hand later redeemers a different deal under the same code name.
            // Active stays editable — it is the kill switch, not a term. This restores the legacy
            // rule (JobDiscountCodes/Admin.cshtml locked amount/type/dates once used), but on the
            // server: legacy only greyed the inputs out and its Edit action wrote them anyway.
            //
            // Compared at DAY granularity, not raw DateTime: the codes are stored in local AZ time
            // while the edit form round-trips them through UTC ISO strings, so an exact comparison
            // would see a phantom offset and reject the Active toggle — the one edit that must
            // still work on a redeemed code.
            var termsChanged =
                code.BAsPercent != isPercent ||
                code.CodeAmount != request.Amount ||
                code.CodeStartDate.Date != request.StartDate.Date ||
                code.CodeEndDate.Date != request.EndDate.Date;

            if (termsChanged)
            {
                throw new InvalidOperationException(
                    $"Discount code '{code.CodeName}' has been redeemed {usageCount} time(s). " +
                    "Its amount, type and dates are locked — only Active may be changed. " +
                    "Create a new code to offer different terms.");
            }
        }
        else
        {
            code.BAsPercent = isPercent;
            code.CodeAmount = request.Amount;
            code.CodeStartDate = request.StartDate;
            code.CodeEndDate = request.EndDate;
        }

        code.Active = request.IsActive;
        // Who last touched the code. Previously only written on create, so an amount change left
        // no actor behind — an integrity lock with no record of who turned the key is half a lock.
        code.LebUserId = userId;
        code.Modified = DateTime.Now;

        await _repository.SaveChangesAsync(cancellationToken);

        return MapToDto(code, usageCount, DateTime.Now);
    }

    public async Task<bool> DeleteDiscountCodeAsync(Guid jobId, int ai, CancellationToken cancellationToken = default)
    {
        var code = await _repository.GetByIdAsync(ai, cancellationToken);
        if (code == null || code.JobId != jobId)
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
                code.Modified = DateTime.Now;
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

    private const string TypePercentage = "Percentage";
    private const string TypeDollarAmount = "DollarAmount";

    /// <summary>
    /// Maps the wire discount type to BAsPercent, rejecting anything else.
    ///
    /// This used to be a bare `request.DiscountType == "Percentage"`, an ordinal case-sensitive
    /// compare with no else-branch: "percentage", "Percent" or any typo silently fell through to
    /// false and turned a percentage code into a flat-dollar one.
    /// </summary>
    private static bool ParseDiscountType(string discountType) => discountType switch
    {
        TypePercentage => true,
        TypeDollarAmount => false,
        _ => throw new InvalidOperationException(
            $"Discount type must be '{TypePercentage}' or '{TypeDollarAmount}'.")
    };

    /// <summary>
    /// The DTO's [Range(0.01, 999999.99)] is not type-aware, so on its own it accepts a
    /// 999999.99% code. DiscountCalculator caps the payout at the base amount so nobody is
    /// over-refunded, but the stored row is nonsense and misreports on every screen.
    /// </summary>
    private static void ValidateAmount(decimal amount, bool isPercent)
    {
        if (isPercent && amount > 100m)
        {
            throw new InvalidOperationException("A percentage discount cannot exceed 100%.");
        }
    }

    private static DiscountCodeDto MapToDto(JobDiscountCodes code, int usageCount, DateTime now)
    {
        return new DiscountCodeDto
        {
            Ai = code.Ai,
            CodeName = code.CodeName,
            DiscountType = code.BAsPercent ? TypePercentage : TypeDollarAmount,
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
