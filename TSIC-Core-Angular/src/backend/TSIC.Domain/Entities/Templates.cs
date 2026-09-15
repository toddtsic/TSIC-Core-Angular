using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Templates
{
    public int TemplateId { get; set; }

    public int StrategyId { get; set; }

    public int BracketSize { get; set; }

    public string Variant { get; set; } = null!;

    public string? Name { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual ICollection<BracketInstances> BracketInstances { get; set; } = new List<BracketInstances>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Strategies Strategy { get; set; } = null!;

    public virtual ICollection<TemplateGames> TemplateGames { get; set; } = new List<TemplateGames>();
}
