using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Send-side persistence for invitations. Deliberately tiny: two writes and nothing else.
/// Take-up is inferred by the search query, never recorded here.
/// </summary>
public class InvitationRepository : IInvitationRepository
{
    private readonly SqlDbContext _context;

    public InvitationRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> RecordSendAsync(
        Guid sourceJobId,
        Guid targetJobId,
        InviteKind kind,
        string subject,
        string bodyTemplate,
        DateTime expiresAt,
        string sentByUserId,
        IReadOnlyCollection<(Guid RegistrationId, InvitationOutcome Outcome)> recipients,
        CancellationToken cancellationToken = default)
    {
        var invitation = new Invitations
        {
            InvitationId = Guid.NewGuid(),
            SourceJobId = sourceJobId,
            TargetJobId = targetJobId,
            InviteKindId = (int)kind,
            Subject = subject,
            BodyTemplate = bodyTemplate,
            ExpiresAt = expiresAt,
            Modified = DateTime.Now,
            LebUserId = sentByUserId
        };
        _context.Invitations.Add(invitation);

        // The composite key is (SourceRegistrationId, InvitationId), so one registration can appear
        // at most once in a send. Collapse duplicates here rather than letting SQL raise a PK
        // violation that would lose the whole batch's audit for a caller-side slip.
        // First occurrence wins; OptedOut is decided before Sent is ever assigned, so ordering the
        // caller supplies is already the meaningful one.
        var seen = new HashSet<Guid>();
        foreach (var (registrationId, outcome) in recipients)
        {
            if (!seen.Add(registrationId)) continue;

            _context.InvitationRegistrations.Add(new InvitationRegistrations
            {
                SourceRegistrationId = registrationId,
                InvitationId = invitation.InvitationId,
                InvitationOutcomeId = (int)outcome,
                Modified = DateTime.Now,
                LebUserId = sentByUserId
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return invitation.InvitationId;
    }

    public async Task MarkOutcomeAsync(
        Guid invitationId,
        IReadOnlyCollection<Guid> registrationIds,
        InvitationOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        if (registrationIds.Count == 0) return;

        // Tracked (not ExecuteUpdate) so the write goes through the same change-tracking path as the
        // rest of the codebase; a send's recipient count is in the hundreds, not the millions.
        var ids = registrationIds.Distinct().ToList();
        var rows = await _context.InvitationRegistrations
            .Where(r => r.InvitationId == invitationId && ids.Contains(r.SourceRegistrationId))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return;

        foreach (var row in rows)
        {
            row.InvitationOutcomeId = (int)outcome;
            row.Modified = DateTime.Now;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
