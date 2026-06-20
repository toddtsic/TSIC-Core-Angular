using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobQuickLink
{
    public Guid JobQuickLinkId { get; set; }

    public Guid JobId { get; set; }

    public string LinkKey { get; set; } = null!;

    public bool? Enabled { get; set; }

    public string? Label { get; set; }

    public int? SortOrder { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual LinkType LinkKeyNavigation { get; set; } = null!;
}
