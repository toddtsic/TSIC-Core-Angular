using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class LinkType
{
    public Guid LinkTypeId { get; set; }

    public string LinkKey { get; set; } = null!;

    public string DefaultLabel { get; set; } = null!;

    public string? DefaultIcon { get; set; }

    public string? RouteTemplate { get; set; }

    public string? NavigateUrl { get; set; }

    public string? Target { get; set; }

    public string? GroundingSetting { get; set; }

    public bool GroundingInverted { get; set; }

    public int DefaultSortOrder { get; set; }

    public bool Active { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual ICollection<JobQuickLink> JobQuickLink { get; set; } = new List<JobQuickLink>();

    public virtual AspNetUsers? LebUser { get; set; }
}
