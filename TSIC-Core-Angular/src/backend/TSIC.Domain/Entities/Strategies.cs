using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Strategies
{
    public int StrategyId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<Templates> Templates { get; set; } = new List<Templates>();
}
