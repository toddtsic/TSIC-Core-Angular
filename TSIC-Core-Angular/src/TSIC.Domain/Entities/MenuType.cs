using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MenuType
{
    public string? LebUserId { get; set; }

    public string? MenuType1 { get; set; }

    public int MenuTypeId { get; set; }

    public DateTime Modified { get; set; }

    public virtual ICollection<JobMenu> JobMenus { get; set; } = new List<JobMenu>();

    public virtual AspNetUser? LebUser { get; set; }
}
