using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Clubs
{
    public int ClubId { get; set; }

    public string ClubName { get; set; } = null!;

    public string? LebUserId { get; set; }

    public DateTime? Modified { get; set; }

    public virtual ICollection<ClubReps> ClubReps { get; set; } = new List<ClubReps>();

    public virtual ICollection<ClubTeams> ClubTeams { get; set; } = new List<ClubTeams>();

    public virtual AspNetUsers? LebUser { get; set; }
}
