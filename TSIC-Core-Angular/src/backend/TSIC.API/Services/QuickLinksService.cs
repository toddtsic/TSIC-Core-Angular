using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services;

/// <summary>
/// Resolves a job's quicklinks (public landing) and serves/saves the SuperUser
/// editor model. Grounding is intentionally NOT resolved here — each link's
/// on/off truth lives in the job pulse, which the client already holds. This
/// service emits config only plus, per link, the camelCase pulse flag the client
/// reads. One grounding implementation, one source of truth.
/// </summary>
public class QuickLinksService : IQuickLinksService
{
    private readonly IQuickLinksRepository _repo;

    /// <summary>
    /// linkKey → (camelCase JobPulse flag, inverted). A null entry (or a linkKey
    /// absent from this map) means ungrounded — "deliberate-on", shown only when a
    /// JobQuickLink row sets Enabled = true.
    ///
    /// Note: `store` is grounded on the richer pulse signal `storeHasActiveItems`
    /// (not the raw column) so it auto-shows exactly as the live hero does today.
    /// `public-rosters` maps to the positive pulse flag `allowRosterViewPlayer`,
    /// so GroundingInverted is normalized to false at this boundary.
    /// </summary>
    private static readonly Dictionary<string, (string PulseFlag, bool Inverted)> GroundingMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["register-player"]   = ("playerRegistrationOpen", false),
            ["register-team"]     = ("teamRegistrationOpen", false),
            ["view-schedule"]     = ("schedulePublished", false),
            ["master-schedule"]   = ("schedulePublished", false),
            ["public-rosters"]    = ("allowRosterViewPlayer", false),
            ["player-insurance"]  = ("offerPlayerRegsaverInsurance", false),
            ["clubrep-insurance"] = ("offerTeamRegsaverInsurance", false),
            ["store"]             = ("storeHasActiveItems", false),
            // "register-coach" is intentionally absent — ungrounded.
        };

    public QuickLinksService(IQuickLinksRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<QuickLinkResolvedDto>> ResolveForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        // Sequential awaits — same scoped DbContext, never Task.WhenAll.
        var jobRows = await _repo.GetJobQuickLinksByJobAsync(jobId, ct);

        // Option A: a job with no overrides is "unconfigured" — return nothing so the
        // landing hero keeps its own pulse-derived defaults (zero regression). The
        // editor becomes authoritative only once at least one override row exists.
        if (jobRows.Count == 0)
            return new List<QuickLinkResolvedDto>();

        var catalog = await _repo.GetActiveLinkTypesAsync(ct);
        var overrides = jobRows.ToDictionary(q => q.LinkKey, StringComparer.OrdinalIgnoreCase);

        var resolved = new List<QuickLinkResolvedDto>(catalog.Count);
        foreach (var lt in catalog)
        {
            overrides.TryGetValue(lt.LinkKey, out var jr);
            var (pulseFlag, inverted) = ResolveGrounding(lt.LinkKey);

            resolved.Add(new QuickLinkResolvedDto
            {
                LinkKey = lt.LinkKey,
                Label = jr?.Label ?? lt.DefaultLabel,
                Icon = lt.DefaultIcon,
                RouteTemplate = lt.RouteTemplate,
                NavigateUrl = lt.NavigateUrl,
                Target = lt.Target,
                SortOrder = jr?.SortOrder ?? lt.DefaultSortOrder,
                GroundingPulseFlag = pulseFlag,
                GroundingInverted = inverted,
                Enabled = jr?.Enabled,
            });
        }

        return resolved
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.LinkKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<QuickLinkEditorModelDto?> GetEditorModelAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobRef = await _repo.GetJobRefAsync(jobId, ct);
        if (jobRef == null)
            return null;

        var catalog = await _repo.GetActiveLinkTypesAsync(ct);
        var jobRows = await _repo.GetJobQuickLinksByJobAsync(jobId, ct);
        var overrides = jobRows.ToDictionary(q => q.LinkKey, StringComparer.OrdinalIgnoreCase);

        var rows = new List<QuickLinkEditorRowDto>(catalog.Count);
        foreach (var lt in catalog)
        {
            overrides.TryGetValue(lt.LinkKey, out var jr);
            var grounded = IsGrounded(lt.LinkKey);
            var (pulseFlag, inverted) = ResolveGrounding(lt.LinkKey);

            rows.Add(new QuickLinkEditorRowDto
            {
                LinkKey = lt.LinkKey,
                DefaultLabel = lt.DefaultLabel,
                OverrideLabel = jr?.Label,
                IsGrounded = grounded,
                GroundingSetting = lt.GroundingSetting,
                GroundingPulseFlag = pulseFlag,
                GroundingInverted = inverted,
                DefaultSortOrder = lt.DefaultSortOrder,
                OverrideSortOrder = jr?.SortOrder,
                EffectiveSortOrder = jr?.SortOrder ?? lt.DefaultSortOrder,
                Icon = lt.DefaultIcon,
                RouteTemplate = lt.RouteTemplate,
                NavigateUrl = lt.NavigateUrl,
                Target = lt.Target,
                Enabled = jr?.Enabled,
                HasJobRow = jr != null,
            });
        }

        return new QuickLinkEditorModelDto
        {
            JobId = jobId,
            JobName = jobRef.JobName,
            JobPath = jobRef.JobPath,
            Rows = rows
                .OrderBy(r => r.EffectiveSortOrder)
                .ThenBy(r => r.LinkKey, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    public async Task SaveAsync(Guid jobId, SaveQuickLinksRequest request, string userId, CancellationToken ct = default)
    {
        // Validate every linkKey against the live catalog before touching the DB
        // so the unique (JobId, LinkKey) constraint is never probed with garbage.
        var catalog = await _repo.GetActiveLinkTypesAsync(ct);
        var byKey = catalog.ToDictionary(lt => lt.LinkKey, StringComparer.OrdinalIgnoreCase);

        foreach (var row in request.Rows)
        {
            if (!byKey.TryGetValue(row.LinkKey, out var lt))
                throw new InvalidOperationException($"Unknown quicklink: {row.LinkKey}");

            var existing = await _repo.GetJobQuickLinkAsync(jobId, row.LinkKey, ct);

            if (row.Delete)
            {
                if (existing != null)
                    _repo.RemoveJobQuickLink(existing);
                continue;
            }

            // Enforce the invariant server-side regardless of client input:
            // grounded links may only follow (null) or force-hide (false) — never force-show.
            var grounded = IsGrounded(row.LinkKey);
            var enabled = grounded ? (row.Enabled == false ? false : (bool?)null) : row.Enabled;

            if (existing == null)
            {
                _repo.AddJobQuickLink(new JobQuickLink
                {
                    JobId = jobId,
                    LinkKey = row.LinkKey,
                    Enabled = enabled,
                    Label = row.Label,
                    SortOrder = row.SortOrder,
                    Modified = DateTime.Now,
                    LebUserId = userId,
                });
            }
            else
            {
                existing.Enabled = enabled;
                existing.Label = row.Label;
                existing.SortOrder = row.SortOrder;
                existing.Modified = DateTime.Now;
                existing.LebUserId = userId;
            }
        }

        await _repo.SaveChangesAsync(ct);
    }

    private static bool IsGrounded(string linkKey) => GroundingMap.ContainsKey(linkKey);

    private static (string? PulseFlag, bool Inverted) ResolveGrounding(string linkKey)
        => GroundingMap.TryGetValue(linkKey, out var g) ? (g.PulseFlag, g.Inverted) : (null, false);
}
