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
        // Get platform default
        var defaultNav = await GetPlatformDefaultAsync(roleId, cancellationToken);
        if (defaultNav == null) return null;

        // Get job override
        var overrideNav = await GetJobOverrideAsync(roleId, jobId, cancellationToken);

        // If no override, return defaults as-is
        if (overrideNav == null) return defaultNav;

        // Merge: default items + override items appended at end
        var mergedItems = new List<NavItemDto>(defaultNav.Items);
        mergedItems.AddRange(overrideNav.Items);

        return new NavDto
        {
            NavId = defaultNav.NavId,
            RoleId = roleId,
            JobId = jobId,
            Active = true,
            Items = mergedItems
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
                Text = ni.Text,
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
                Text = ni.Text,
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
