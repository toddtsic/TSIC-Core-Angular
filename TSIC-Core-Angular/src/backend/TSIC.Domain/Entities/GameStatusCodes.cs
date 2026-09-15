using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class GameStatusCodes
{
    public int GStatusCode { get; set; }

    public string? GStatusText { get; set; }

    public virtual ICollection<Schedule> Schedule { get; set; } = new List<Schedule>();
}
