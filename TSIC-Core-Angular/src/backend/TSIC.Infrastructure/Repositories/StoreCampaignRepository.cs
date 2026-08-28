using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Audience queries for the store's three email campaigns.
/// </summary>
public class StoreCampaignRepository : IStoreCampaignRepository
{
    private readonly SqlDbContext _context;

    public StoreCampaignRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<StoreAbandonedCartRowDto>> GetAbandonedCartsAsync(
        int storeId, int minAgeHours, int maxAgeHours, CancellationToken cancellationToken = default)
    {
        // Legacy measured the window against the LINE's Modified stamp but displayed the BATCH's —
        // a cart whose last edit was inside the window shows the batch date. Preserved verbatim.
        var now = DateTime.Now;

        var rows = await _context.StoreCartBatchSkus
            .AsNoTracking()
            .Where(cbs =>
                cbs.Active
                && cbs.StoreCartBatch.StoreCart.StoreId == storeId
                && !cbs.StoreCartBatch.StoreCartBatchAccounting.Any()
                && EF.Functions.DateDiffHour(cbs.Modified, now) >= minAgeHours
                && EF.Functions.DateDiffHour(cbs.Modified, now) <= maxAgeHours)
            .OrderByDescending(cbs => cbs.StoreCartBatch.Modified)
            .Select(cbs => new
            {
                cbs.StoreCartBatchId,
                BatchDate = cbs.StoreCartBatch.Modified,
                FamilyUserName = cbs.StoreCartBatch.StoreCart.FamilyUser.UserName,
                cbs.StoreCartBatch.StoreCart.FamilyUserId,
                cbs.StoreSkuId,
                cbs.Quantity,
                ItemName = cbs.StoreSku.StoreItem.StoreItemName,
                ColorName = cbs.StoreSku.StoreColor != null ? cbs.StoreSku.StoreColor.StoreColorName : null,
                SizeName = cbs.StoreSku.StoreSize != null ? cbs.StoreSku.StoreSize.StoreSizeName : null,
                FirstName = cbs.DirectToReg != null && cbs.DirectToReg.User != null ? cbs.DirectToReg.User.FirstName : null,
                LastName = cbs.DirectToReg != null && cbs.DirectToReg.User != null ? cbs.DirectToReg.User.LastName : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.StoreCartBatchId)
            .Select(g => new StoreAbandonedCartRowDto
            {
                BatchId = g.Key,
                BatchDate = g.First().BatchDate,
                FamilyUserName = g.First().FamilyUserName ?? string.Empty,
                FamilyUserId = g.First().FamilyUserId,
                Lines = g.Select(r => new StoreAbandonedCartLineDto
                {
                    StoreSkuId = r.StoreSkuId,
                    Quantity = r.Quantity,
                    Label = BuildLineLabel(r.Quantity, r.ItemName, r.ColorName, r.SizeName, r.FirstName, r.LastName)
                }).ToList()
            })
            .OrderByDescending(c => c.BatchDate)
            .ToList();
    }

    public async Task<List<string>> GetFamilyUserIdsNeverOrderedAsync(
        Guid jobId, int storeId, CancellationToken cancellationToken = default)
    {
        // Composed as one statement (legacy pulled every used family id into memory first, then
        // sent it back as a giant IN list — which silently truncates once a store gets busy).
        var used = _context.StoreCart
            .AsNoTracking()
            .Where(sc => sc.StoreId == storeId)
            .Select(sc => sc.FamilyUserId);

        return await _context.Registrations
            .AsNoTracking()
            .Where(r =>
                r.JobId == jobId
                && r.BActive == true
                && r.FamilyUserId != null
                && !used.Contains(r.FamilyUserId))
            .Select(r => r.FamilyUserId!)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetFamilyUserIdsThatOrderedAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchAccounting
            .AsNoTracking()
            .Where(scba => scba.StoreCartBatch.StoreCart.StoreId == storeId)
            .Select(scba => scba.StoreCartBatch.StoreCart.FamilyUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<StoreCampaignFamilyDto>> GetFamilyContactsAsync(
        Guid jobId, IReadOnlyCollection<string> familyUserIds, CancellationToken cancellationToken = default)
    {
        if (familyUserIds.Count == 0) return [];

        var ids = familyUserIds.Distinct().ToList();

        var families = await _context.Families
            .AsNoTracking()
            .Where(f => ids.Contains(f.FamilyUserId))
            .Select(f => new
            {
                f.FamilyUserId,
                FamilyUserName = f.FamilyUser.UserName,
                f.MomEmail,
                f.DadEmail
            })
            .ToListAsync(cancellationToken);

        // Registration anchor + opt-out roll-up, one query for the whole audience.
        var regs = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId != null && ids.Contains(r.FamilyUserId))
            .Select(r => new { FamilyUserId = r.FamilyUserId!, r.RegistrationId, r.BemailOptOut, r.BActive })
            .ToListAsync(cancellationToken);

        var regsByFamily = regs
            .GroupBy(r => r.FamilyUserId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return families.Select(f =>
        {
            regsByFamily.TryGetValue(f.FamilyUserId, out var familyRegs);

            // Prefer an ACTIVE registration as the substitution anchor — a dropped registrant's
            // token render would describe a registration the family no longer holds.
            var anchor = familyRegs?.FirstOrDefault(r => r.BActive == true) ?? familyRegs?.FirstOrDefault();

            return new StoreCampaignFamilyDto
            {
                FamilyUserId = f.FamilyUserId,
                FamilyUserName = f.FamilyUserName ?? string.Empty,
                MomEmail = f.MomEmail,
                DadEmail = f.DadEmail,
                RepresentativeRegistrationId = anchor?.RegistrationId,
                OptedOut = familyRegs?.Any(r => r.BemailOptOut) == true
            };
        }).ToList();
    }

    /// <summary>
    /// Legacy's <c>SkuQuantityNamePlayer</c> string. Legacy interpolated the parts unconditionally,
    /// so a line with no color, no size, or no DirectTo registrant rendered stray dashes and a
    /// trailing " for  ". The pieces are joined here instead, so a partial SKU reads cleanly.
    /// </summary>
    private static string BuildLineLabel(
        int quantity, string? itemName, string? colorName, string? sizeName, string? firstName, string? lastName)
    {
        var descriptor = string.Join("-", new[] { itemName, colorName, sizeName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

        var person = string.Join(" ", new[] { firstName, lastName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

        var label = $"{quantity} {descriptor}".Trim();
        return string.IsNullOrEmpty(person) ? label : $"{label} for {person}";
    }
}
