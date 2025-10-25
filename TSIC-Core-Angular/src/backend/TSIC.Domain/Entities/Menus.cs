using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Menus
{
    public Guid MenuId { get; set; }

    public string? RoleId { get; set; }

    public bool Active { get; set; }

    public Guid JobId { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual ICollection<MenuItems> MenuItems { get; set; } = new List<MenuItems>();

    public virtual AspNetRoles? Role { get; set; }
}
