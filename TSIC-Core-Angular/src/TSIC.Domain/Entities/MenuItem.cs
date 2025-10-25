using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MenuItem
{
    public Guid MenuItemId { get; set; }

    public Guid MenuId { get; set; }

    public Guid? ParentMenuItemId { get; set; }

    public bool Active { get; set; }

    public int? Index { get; set; }

    public string? ModuleName { get; set; }

    public string? RouteName { get; set; }

    public string? NavigateUrl { get; set; }

    public string? MenuText { get; set; }

    public string? IconName { get; set; }

    public virtual ICollection<MenuItem> InverseParentMenuItem { get; set; } = new List<MenuItem>();

    public virtual Menu Menu { get; set; } = null!;

    public virtual MenuItem? ParentMenuItem { get; set; }
}
