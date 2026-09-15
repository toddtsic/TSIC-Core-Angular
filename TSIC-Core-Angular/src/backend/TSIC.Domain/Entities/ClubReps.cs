using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClubReps
{
    public int Aid { get; set; }

    public int ClubId { get; set; }

    public string ClubRepUserId { get; set; } = null!;

    public string? LebUserId { get; set; }

    public DateTime? Modified { get; set; }

    public virtual Clubs Club { get; set; } = null!;

    public virtual AspNetUsers ClubRepUser { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
