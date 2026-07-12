using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Domain.Constants;

namespace TSIC.Infrastructure.Repositories;

public class FamiliesRepository : IFamiliesRepository
{
    private readonly SqlDbContext _context;

    public FamiliesRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<Families?> GetByFamilyUserIdAsync(string familyUserId, CancellationToken cancellationToken = default)
    {
        return await _context.Families
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.FamilyUserId == familyUserId, cancellationToken);
    }

    public async Task<List<string>> GetEmailsForFamilyAndPlayersAsync(Guid jobId, string familyUserId, CancellationToken cancellationToken = default)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fam = await _context.Families.AsNoTracking().FirstOrDefaultAsync(f => f.FamilyUserId == familyUserId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(fam?.MomEmail)) recipients.Add(fam!.MomEmail!.Trim());
        if (!string.IsNullOrWhiteSpace(fam?.DadEmail)) recipients.Add(fam!.DadEmail!.Trim());

        var playerEmails = await _context.Registrations.AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId)
            .Select(r => r.User!.Email)
            .ToListAsync(cancellationToken);
        foreach (var e in playerEmails)
        {
            var norm = e?.Trim();
            if (!string.IsNullOrWhiteSpace(norm)) recipients.Add(norm!);
        }
        return recipients
            .Select(x => x.Trim())
            .Where(EmailAddressRules.IsSendable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<BatchFamilyEmailsDto>> GetByFamilyUserIdsAsync(
        IEnumerable<string> familyUserIds, CancellationToken cancellationToken = default)
    {
        var ids = familyUserIds.Distinct().ToList();
        if (ids.Count == 0) return new List<BatchFamilyEmailsDto>();

        return await _context.Families
            .AsNoTracking()
            .Where(f => ids.Contains(f.FamilyUserId))
            .Select(f => new BatchFamilyEmailsDto
            {
                FamilyUserId = f.FamilyUserId,
                MomEmail = f.MomEmail,
                DadEmail = f.DadEmail
            })
            .ToListAsync(cancellationToken);
    }

    public void Add(Families family)
    {
        _context.Families.Add(family);
    }

    public void Update(Families family)
    {
        _context.Families.Update(family);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
