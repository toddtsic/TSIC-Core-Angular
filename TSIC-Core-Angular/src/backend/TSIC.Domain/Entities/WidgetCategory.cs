using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class WidgetCategory
{
    public int CategoryId { get; set; }

    public string Name { get; set; } = null!;

    public string Workspace { get; set; } = null!;

    public string? Icon { get; set; }

    public int DefaultOrder { get; set; }

    public virtual ICollection<JobWidget> JobWidget { get; set; } = new List<JobWidget>();

    public virtual ICollection<UserWidget> UserWidget { get; set; } = new List<UserWidget>();

    public virtual ICollection<Widget> Widget { get; set; } = new List<Widget>();

    public virtual ICollection<WidgetDefault> WidgetDefault { get; set; } = new List<WidgetDefault>();
}
