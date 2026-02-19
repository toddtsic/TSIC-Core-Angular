using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class NavItem
{
    public int NavItemId { get; set; }

    public int NavId { get; set; }

    public int? ParentNavItemId { get; set; }

    public bool Active { get; set; }

    public int SortOrder { get; set; }

    public string Text { get; set; } = null!;

    public string? IconName { get; set; }

    public string? RouterLink { get; set; }

    public string? NavigateUrl { get; set; }

    public string? Target { get; set; }

    public DateTime Modified { get; set; }

    public string? ModifiedBy { get; set; }

    public virtual ICollection<NavItem> InverseParentNavItem { get; set; } = new List<NavItem>();

    public virtual AspNetUsers? ModifiedByNavigation { get; set; }

    public virtual Nav Nav { get; set; } = null!;

    public virtual NavItem? ParentNavItem { get; set; }
}
