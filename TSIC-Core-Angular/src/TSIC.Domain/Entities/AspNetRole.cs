using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AspNetRole
{
    public string? ConcurrencyStamp { get; set; }

    public string Id { get; set; } = null!;

    public string? Name { get; set; }

    public string? NormalizedName { get; set; }

    public virtual ICollection<AspNetRoleClaim> AspNetRoleClaims { get; set; } = new List<AspNetRoleClaim>();

    public virtual ICollection<JobMenu> JobMenus { get; set; } = new List<JobMenu>();

    public virtual ICollection<JobMessage> JobMessages { get; set; } = new List<JobMessage>();

    public virtual ICollection<Menu> Menus { get; set; } = new List<Menu>();

    public virtual ICollection<RegForm> RegForms { get; set; } = new List<RegForm>();

    public virtual ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    public virtual ICollection<AspNetUser> Users { get; set; } = new List<AspNetUser>();
}
