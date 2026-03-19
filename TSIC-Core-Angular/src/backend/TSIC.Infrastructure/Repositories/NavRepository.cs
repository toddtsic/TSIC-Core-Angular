using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Read-only repository for nav rendering.
/// Merges platform defaults with job-specific overrides.
/// </summary>
public class NavRepository : INavRepository
{
    private readonly SqlDbContext _context;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public NavRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<NavDto?> GetPlatformDefaultAsync(
        string roleId,
        CancellationToken cancellationToken = default)
    {
        var nav = await _context.Nav
            .AsNoTracking()
            .Where(n => n.RoleId == roleId && n.JobId == null && n.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (nav == null) return null;

        var items = await LoadNavItemTreeAsync(nav.NavId, activeOnly: true, jobNavCtx: null, cancellationToken);

        return new NavDto
        {
            NavId = nav.NavId,
            RoleId = nav.RoleId,
            JobId = null,
            Active = nav.Active,
            Items = items
        };
    }

    public async Task<NavDto?> GetJobOverrideAsync(
        string roleId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var nav = await _context.Nav
            .AsNoTracking()
            .Where(n => n.RoleId == roleId && n.JobId == jobId && n.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (nav == null) return null;

        var items = await LoadNavItemTreeAsync(nav.NavId, activeOnly: true, jobNavCtx: null, cancellationToken);

        return new NavDto
        {
            NavId = nav.NavId,
            RoleId = nav.RoleId,
            JobId = nav.JobId,
            Active = nav.Active,
            Items = items
        };
    }

    public async Task<NavDto?> GetMergedNavAsync(
        string roleId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // 0. Fetch job metadata for visibility rule evaluation
        var jobCtx = await GetJobNavContextAsync(jobId, cancellationToken);

        // 1. Get platform default nav record
        var defaultNav = await _context.Nav
            .AsNoTracking()
            .Where(n => n.RoleId == roleId && n.JobId == null && n.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultNav == null) return null;

        // 2. Load platform default items as a mutable tree (filtered by visibility rules)
        var defaultItems = await LoadNavItemTreeAsync(defaultNav.NavId, activeOnly: true, jobCtx, cancellationToken);

        // 3. Check for job override nav
        var overrideNav = await _context.Nav
            .AsNoTracking()
            .Where(n => n.RoleId == roleId && n.JobId == jobId && n.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (overrideNav == null)
        {
            return new NavDto
            {
                NavId = defaultNav.NavId,
                RoleId = roleId,
                JobId = jobId,
                Active = true,
                Items = defaultItems
            };
        }

        // 4. Load ALL override items (including inactive hide rows)
        var overrideItems = await _context.NavItem
            .AsNoTracking()
            .Where(ni => ni.NavId == overrideNav.NavId)
            .ToListAsync(cancellationToken);

        // 5. Build suppressed set from hide rows (DefaultNavItemId set + Active=false)
        var suppressedIds = overrideItems
            .Where(ni => ni.DefaultNavItemId != null && !ni.Active)
            .Select(ni => ni.DefaultNavItemId!.Value)
            .ToHashSet();

        // 6. Filter default items — cascade: suppressing an L1 also suppresses its children
        var filteredItems = new List<NavItemDto>();
        foreach (var root in defaultItems)
        {
            if (suppressedIds.Contains(root.NavItemId)) continue; // L1 suppressed — skip entire section

            var filteredChildren = root.Children
                .Where(c => !suppressedIds.Contains(c.NavItemId))
                .ToList();

            filteredItems.Add(root with { Children = filteredChildren });
        }

        // 7a. Slot additive override items under a default parent (DefaultParentNavItemId set)
        var slotted = overrideItems
            .Where(ni => ni.DefaultParentNavItemId != null && ni.Active)
            .OrderBy(ni => ni.SortOrder)
            .ToList();

        foreach (var item in slotted)
        {
            var parent = filteredItems.FirstOrDefault(r => r.NavItemId == item.DefaultParentNavItemId!.Value);
            if (parent == null) continue; // parent was suppressed or not found

            parent.Children.Add(new NavItemDto
            {
                NavItemId = item.NavItemId,
                ParentNavItemId = item.DefaultParentNavItemId,
                SortOrder = item.SortOrder,
                Text = item.Text ?? string.Empty,
                IconName = item.IconName,
                RouterLink = item.RouterLink,
                NavigateUrl = item.NavigateUrl,
                Target = item.Target,
                Active = true,
                Children = new List<NavItemDto>()
            });
        }

        // 7b. New root sections from override (no default cols, ParentNavItemId null, Active=true)
        var newRoots = overrideItems
            .Where(ni => ni.DefaultNavItemId == null && ni.DefaultParentNavItemId == null
                      && ni.ParentNavItemId == null && ni.Active)
            .OrderBy(ni => ni.SortOrder)
            .ToList();

        var newRootIds = newRoots.Select(r => r.NavItemId).ToHashSet();
        var newRootChildren = overrideItems
            .Where(ni => ni.ParentNavItemId != null && newRootIds.Contains(ni.ParentNavItemId.Value) && ni.Active)
            .ToList();

        foreach (var newRoot in newRoots)
        {
            var children = newRootChildren
                .Where(c => c.ParentNavItemId == newRoot.NavItemId)
                .OrderBy(c => c.SortOrder)
                .Select(c => new NavItemDto
                {
                    NavItemId = c.NavItemId,
                    ParentNavItemId = c.ParentNavItemId,
                    SortOrder = c.SortOrder,
                    Text = c.Text ?? string.Empty,
                    IconName = c.IconName,
                    RouterLink = c.RouterLink,
                    NavigateUrl = c.NavigateUrl,
                    Target = c.Target,
                    Active = true,
                    Children = new List<NavItemDto>()
                })
                .ToList();

            filteredItems.Add(new NavItemDto
            {
                NavItemId = newRoot.NavItemId,
                ParentNavItemId = null,
                SortOrder = newRoot.SortOrder,
                Text = newRoot.Text ?? string.Empty,
                IconName = newRoot.IconName,
                RouterLink = newRoot.RouterLink,
                NavigateUrl = newRoot.NavigateUrl,
                Target = newRoot.Target,
                Active = true,
                Children = children
            });
        }

        return new NavDto
        {
            NavId = defaultNav.NavId,
            RoleId = roleId,
            JobId = jobId,
            Active = true,
            Items = filteredItems
        };
    }

    // ─── Private helpers ────────────────────────────────────────────

    private sealed record JobNavContext(string? SportName, string? JobTypeName, string? CustomerName);

    private async Task<JobNavContext?> GetJobNavContextAsync(Guid jobId, CancellationToken ct)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobNavContext(
                j.Sport.SportName,
                j.JobType.JobTypeName,
                j.Customer.CustomerName))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Evaluates visibility rules against job metadata.
    /// Returns true if the item should be visible for this job.
    /// </summary>
    private static bool PassesVisibilityRules(string? rulesJson, JobNavContext ctx)
    {
        if (string.IsNullOrEmpty(rulesJson)) return true;

        NavItemVisibilityRules? rules;
        try
        {
            rules = JsonSerializer.Deserialize<NavItemVisibilityRules>(rulesJson, JsonOpts);
        }
        catch
        {
            return true; // Malformed JSON = fail-open
        }

        if (rules == null) return true;

        // Sports allowlist
        if (rules.Sports is { Count: > 0 } && ctx.SportName != null
            && !rules.Sports.Contains(ctx.SportName, StringComparer.OrdinalIgnoreCase))
            return false;

        // JobTypes allowlist
        if (rules.JobTypes is { Count: > 0 } && ctx.JobTypeName != null
            && !rules.JobTypes.Contains(ctx.JobTypeName, StringComparer.OrdinalIgnoreCase))
            return false;

        // CustomersDeny denylist
        if (rules.CustomersDeny is { Count: > 0 } && ctx.CustomerName != null
            && rules.CustomersDeny.Contains(ctx.CustomerName, StringComparer.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Loads nav items as a 2-level tree. When jobNavCtx is provided,
    /// filters items by visibility rules before building the tree.
    /// </summary>
    private async Task<List<NavItemDto>> LoadNavItemTreeAsync(
        int navId,
        bool activeOnly,
        JobNavContext? jobNavCtx,
        CancellationToken cancellationToken)
    {
        // Load raw entities so we have access to VisibilityRules
        var query = _context.NavItem
            .AsNoTracking()
            .Where(ni => ni.NavId == navId);

        if (activeOnly)
            query = query.Where(ni => ni.Active);

        var allItems = await query.ToListAsync(cancellationToken);

        // Separate roots and children
        var roots = allItems
            .Where(ni => ni.ParentNavItemId == null)
            .OrderBy(ni => ni.SortOrder)
            .ToList();

        var children = allItems
            .Where(ni => ni.ParentNavItemId != null)
            .OrderBy(ni => ni.SortOrder)
            .ToList();

        // Build tree with optional visibility filtering
        var result = new List<NavItemDto>();

        foreach (var root in roots)
        {
            // Apply visibility rules to L1 items — failure removes entire section
            if (jobNavCtx != null && !PassesVisibilityRules(root.VisibilityRules, jobNavCtx))
                continue;

            var rootDto = new NavItemDto
            {
                NavItemId = root.NavItemId,
                ParentNavItemId = null,
                SortOrder = root.SortOrder,
                Text = root.Text ?? string.Empty,
                IconName = root.IconName,
                RouterLink = root.RouterLink,
                NavigateUrl = root.NavigateUrl,
                Target = root.Target,
                Active = root.Active,
                Children = new List<NavItemDto>()
            };

            // Add children, filtering by visibility rules
            foreach (var child in children.Where(c => c.ParentNavItemId == root.NavItemId))
            {
                if (jobNavCtx != null && !PassesVisibilityRules(child.VisibilityRules, jobNavCtx))
                    continue;

                rootDto.Children.Add(new NavItemDto
                {
                    NavItemId = child.NavItemId,
                    ParentNavItemId = child.ParentNavItemId,
                    SortOrder = child.SortOrder,
                    Text = child.Text ?? string.Empty,
                    IconName = child.IconName,
                    RouterLink = child.RouterLink,
                    NavigateUrl = child.NavigateUrl,
                    Target = child.Target,
                    Active = child.Active,
                    Children = new List<NavItemDto>()
                });
            }

            result.Add(rootDto);
        }

        return result;
    }
}
