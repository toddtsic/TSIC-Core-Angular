using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Read-only repository for nav rendering.
/// Merges platform defaults with job-specific overrides.
/// </summary>
public class NavRepository : INavRepository
{
    private readonly SqlDbContext _context;

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

        var items = await LoadNavItemTreeAsync(nav.NavId, activeOnly: true, cancellationToken);

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

        var items = await LoadNavItemTreeAsync(nav.NavId, activeOnly: true, cancellationToken);

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
        // 1. Get platform default nav record
        var defaultNav = await _context.Nav
            .AsNoTracking()
            .Where(n => n.RoleId == roleId && n.JobId == null && n.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultNav == null) return null;

        // 2. Load platform default items as a mutable tree
        var defaultItems = await LoadNavItemTreeAsync(defaultNav.NavId, activeOnly: true, cancellationToken);

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

    private async Task<List<NavItemDto>> LoadNavItemTreeAsync(
        int navId,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        var query = _context.NavItem
            .AsNoTracking()
            .Where(ni => ni.NavId == navId);

        if (activeOnly)
        {
            query = query.Where(ni => ni.Active);
        }

        // Load root items
        var rootItems = await query
            .Where(ni => ni.ParentNavItemId == null)
            .OrderBy(ni => ni.SortOrder)
            .Select(ni => new NavItemDto
            {
                NavItemId = ni.NavItemId,
                ParentNavItemId = null,
                SortOrder = ni.SortOrder,
                Text = ni.Text ?? string.Empty,
                IconName = ni.IconName,
                RouterLink = ni.RouterLink,
                NavigateUrl = ni.NavigateUrl,
                Target = ni.Target,
                Active = ni.Active,
                Children = new List<NavItemDto>()
            })
            .ToListAsync(cancellationToken);

        if (rootItems.Count == 0) return rootItems;

        // Load all children in one query
        var rootIds = rootItems.Select(r => r.NavItemId).ToList();

        var childQuery = _context.NavItem
            .AsNoTracking()
            .Where(ni => ni.NavId == navId && ni.ParentNavItemId != null && rootIds.Contains(ni.ParentNavItemId.Value));

        if (activeOnly)
        {
            childQuery = childQuery.Where(ni => ni.Active);
        }

        var childItems = await childQuery
            .OrderBy(ni => ni.SortOrder)
            .Select(ni => new NavItemDto
            {
                NavItemId = ni.NavItemId,
                ParentNavItemId = ni.ParentNavItemId,
                SortOrder = ni.SortOrder,
                Text = ni.Text ?? string.Empty,
                IconName = ni.IconName,
                RouterLink = ni.RouterLink,
                NavigateUrl = ni.NavigateUrl,
                Target = ni.Target,
                Active = ni.Active,
                Children = new List<NavItemDto>()
            })
            .ToListAsync(cancellationToken);

        // Populate children into parents
        foreach (var child in childItems)
        {
            var parent = rootItems.FirstOrDefault(r => r.NavItemId == child.ParentNavItemId);
            parent?.Children.Add(child);
        }

        return rootItems;
    }
}
