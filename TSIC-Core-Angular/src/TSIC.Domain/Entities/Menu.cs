using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Menu
{
    public Guid MenuId { get; set; }

    public string? RoleId { get; set; }

    public bool Active { get; set; }

    public Guid JobId { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();

    public virtual AspNetRole? Role { get; set; }
}
