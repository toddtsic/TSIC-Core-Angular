using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Widget
{
    public int WidgetId { get; set; }

    public string Name { get; set; } = null!;

    public string WidgetType { get; set; } = null!;

    public string ComponentKey { get; set; } = null!;

    public int CategoryId { get; set; }

    public string? Description { get; set; }

    public virtual WidgetCategory Category { get; set; } = null!;

    public virtual ICollection<JobWidget> JobWidget { get; set; } = new List<JobWidget>();

    public virtual ICollection<WidgetDefault> WidgetDefault { get; set; } = new List<WidgetDefault>();
}
