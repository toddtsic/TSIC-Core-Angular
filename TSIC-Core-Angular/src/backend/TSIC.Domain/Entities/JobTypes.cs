using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobTypes
{
    public string? JobTypeDesc { get; set; }

    public int JobTypeId { get; set; }

    public string? JobTypeName { get; set; }

    public virtual ICollection<Jobs> Jobs { get; set; } = new List<Jobs>();

    public virtual ICollection<WidgetDefault> WidgetDefault { get; set; } = new List<WidgetDefault>();
}
