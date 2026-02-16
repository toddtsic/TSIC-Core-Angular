using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobWidget
{
    public int JobWidgetId { get; set; }

    public Guid JobId { get; set; }

    public int WidgetId { get; set; }

    public string RoleId { get; set; } = null!;

    public int CategoryId { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsEnabled { get; set; }

    public string? Config { get; set; }

    public virtual WidgetCategory Category { get; set; } = null!;

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetRoles Role { get; set; } = null!;

    public virtual Widget Widget { get; set; } = null!;
}
