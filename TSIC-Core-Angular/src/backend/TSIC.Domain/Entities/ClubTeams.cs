using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClubTeams
{
    public int ClubTeamId { get; set; }

    public int ClubId { get; set; }

    public string ClubTeamName { get; set; } = null!;

    public string ClubTeamGradYear { get; set; } = null!;

    public string? ClubTeamLevelOfPlay { get; set; }

    public string? LebUserId { get; set; }

    public DateTime? Modified { get; set; }

    public bool? Active { get; set; }

    public virtual Clubs Club { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<Teams> Teams { get; set; } = new List<Teams>();
}
