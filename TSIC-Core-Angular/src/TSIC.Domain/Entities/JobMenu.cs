using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobMenu
{
    public string? RoleId { get; set; }

    public bool Active { get; set; }

    public Guid JobId { get; set; }

    public string? LebUserId { get; set; }

    public Guid MenuId { get; set; }

    public int MenuTypeId { get; set; }

    public DateTime Modified { get; set; }

    public string? Tag { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual ICollection<JobMenuItem> JobMenuItems { get; set; } = new List<JobMenuItem>();

    public virtual AspNetUser? LebUser { get; set; }

    public virtual MenuType MenuType { get; set; } = null!;

    public virtual AspNetRole? Role { get; set; }
}
