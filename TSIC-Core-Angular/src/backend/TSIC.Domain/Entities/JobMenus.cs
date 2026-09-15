using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobMenus
{
    public string? RoleId { get; set; }

    public bool Active { get; set; }

    public Guid JobId { get; set; }

    public string? LebUserId { get; set; }

    public Guid MenuId { get; set; }

    public int MenuTypeId { get; set; }

    public DateTime Modified { get; set; }

    public string? Tag { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual ICollection<JobMenuItems> JobMenuItems { get; set; } = new List<JobMenuItems>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual MenuTypes MenuType { get; set; } = null!;

    public virtual AspNetRoles? Role { get; set; }
}
