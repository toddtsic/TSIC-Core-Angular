using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Slide
{
    public Guid SlideId { get; set; }

    public Guid? SliderId { get; set; }

    public int Index { get; set; }

    public string? ImageUrl { get; set; }

    public string? Title { get; set; }

    public string? Subtitle { get; set; }

    public string? Text { get; set; }

    public virtual Slider? Slider { get; set; }
}
