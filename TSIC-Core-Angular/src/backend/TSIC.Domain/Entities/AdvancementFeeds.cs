using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AdvancementFeeds
{
    public int AdvancementFeedId { get; set; }

    public int BracketInstanceId { get; set; }

    public int SourceGid { get; set; }

    public string SourceResult { get; set; } = null!;

    public int TargetGid { get; set; }

    public byte TargetSlot { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual BracketInstances BracketInstance { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Schedule SourceG { get; set; } = null!;

    public virtual Schedule TargetG { get; set; } = null!;
}
