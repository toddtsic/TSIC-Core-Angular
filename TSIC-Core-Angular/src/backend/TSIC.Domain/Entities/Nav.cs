using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Nav
{
    public int NavId { get; set; }

    public string RoleId { get; set; } = null!;

    public Guid? JobId { get; set; }

    public bool Active { get; set; }

    public DateTime Modified { get; set; }

    public string? ModifiedBy { get; set; }

    public virtual Jobs? Job { get; set; }

    public virtual AspNetUsers? ModifiedByNavigation { get; set; }

    public virtual ICollection<NavItem> NavItem { get; set; } = new List<NavItem>();

    public virtual AspNetRoles Role { get; set; } = null!;
}
