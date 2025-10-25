using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class GameStatusCode
{
    public int GStatusCode { get; set; }

    public string? GStatusText { get; set; }

    public virtual ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();
}
