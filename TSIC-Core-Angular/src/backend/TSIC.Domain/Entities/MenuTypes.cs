using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MenuTypes
{
    public string? LebUserId { get; set; }

    public string? MenuType { get; set; }

    public int MenuTypeId { get; set; }

    public DateTime Modified { get; set; }

    public virtual ICollection<JobMenus> JobMenus { get; set; } = new List<JobMenus>();

    public virtual AspNetUsers? LebUser { get; set; }
}
