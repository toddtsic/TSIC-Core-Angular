using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class Sliders
{
    public Guid SliderId { get; set; }

    public Guid? JobId { get; set; }

    public string? BackgroundImageUrl { get; set; }

    public virtual Jobs? Job { get; set; }

    public virtual ICollection<Slides> Slides { get; set; } = new List<Slides>();
}
