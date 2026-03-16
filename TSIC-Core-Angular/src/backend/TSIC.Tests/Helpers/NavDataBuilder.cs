using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Fluent builder for seeding nav test data.
/// IDs are assigned explicitly so tests can assert against known values.
///
/// Usage:
///   var b = new NavDataBuilder(context);
///   var nav     = b.AddPlatformDefaultNav("Director");
///   var search  = b.AddL1Item(nav.NavId, "Search");
///   var teams   = b.AddL2Item(nav.NavId, search.NavItemId, "Teams");
///   var jobNav  = b.AddJobOverrideNav(jobId, "Director");
///   b.AddHideRow(jobNav.NavId, teams.NavItemId);
///   await b.SaveAsync();
/// </summary>
public class NavDataBuilder
{
    private readonly SqlDbContext _context;
    private int _nextId = 1;

    public NavDataBuilder(SqlDbContext context) => _context = context;

    private int NextId() => _nextId++;

    /// <summary>Adds a platform default nav (JobId = null) for the given role.</summary>
    public Nav AddPlatformDefaultNav(string roleId)
    {
        var nav = new Nav
        {
            NavId = NextId(),
            RoleId = roleId,
            JobId = null,
            Active = true,
            Modified = DateTime.UtcNow
        };
        _context.Nav.Add(nav);
        return nav;
    }

    /// <summary>Adds a job override nav (JobId set) for the given role and job.</summary>
    public Nav AddJobOverrideNav(Guid jobId, string roleId)
    {
        var nav = new Nav
        {
            NavId = NextId(),
            RoleId = roleId,
            JobId = jobId,
            Active = true,
            Modified = DateTime.UtcNow
        };
        _context.Nav.Add(nav);
        return nav;
    }

    /// <summary>Adds a Level 1 (root) nav item to the given nav.</summary>
    public NavItem AddL1Item(int navId, string text)
    {
        var item = new NavItem
        {
            NavItemId = NextId(),
            NavId = navId,
            ParentNavItemId = null,
            Active = true,
            SortOrder = _nextId * 10,
            Text = text,
            Modified = DateTime.UtcNow
        };
        _context.NavItem.Add(item);
        return item;
    }

    /// <summary>Adds a Level 2 (child) nav item under the given parent.</summary>
    public NavItem AddL2Item(int navId, int parentNavItemId, string text)
    {
        var item = new NavItem
        {
            NavItemId = NextId(),
            NavId = navId,
            ParentNavItemId = parentNavItemId,
            Active = true,
            SortOrder = _nextId * 10,
            Text = text,
            Modified = DateTime.UtcNow
        };
        _context.NavItem.Add(item);
        return item;
    }

    /// <summary>
    /// Adds a hide row to the override nav.
    /// Active = false signals "suppress this default item".
    /// </summary>
    public NavItem AddHideRow(int navId, int defaultNavItemId)
    {
        var item = new NavItem
        {
            NavItemId = NextId(),
            NavId = navId,
            DefaultNavItemId = defaultNavItemId,
            Active = false,
            SortOrder = 0,
            Modified = DateTime.UtcNow
        };
        _context.NavItem.Add(item);
        return item;
    }

    /// <summary>Persists all pending additions to the InMemory database.</summary>
    public async Task SaveAsync() => await _context.SaveChangesAsync();
}
