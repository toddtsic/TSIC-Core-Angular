using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class WidgetDefault
{
    public int WidgetDefaultId { get; set; }

    public int JobTypeId { get; set; }

    public string RoleId { get; set; } = null!;

    public int WidgetId { get; set; }

    public int CategoryId { get; set; }

    public int DisplayOrder { get; set; }

    public string? Config { get; set; }

    public virtual WidgetCategory Category { get; set; } = null!;

    public virtual JobTypes JobType { get; set; } = null!;

    public virtual AspNetRoles Role { get; set; } = null!;

    public virtual Widget Widget { get; set; } = null!;
}
