using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Slider
{
    public Guid SliderId { get; set; }

    public Guid? JobId { get; set; }

    public string? BackgroundImageUrl { get; set; }

    public virtual Job? Job { get; set; }

    public virtual ICollection<Slide> Slides { get; set; } = new List<Slide>();
}
