using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamGalleryPhoto
{
    public Guid PhotoId { get; set; }

    public Guid TeamId { get; set; }

    public string UserId { get; set; } = null!;

    public string Caption { get; set; } = null!;

    public DateTime CreateDate { get; set; }

    public virtual Team Team { get; set; } = null!;

    public virtual AspNetUser User { get; set; } = null!;
}
