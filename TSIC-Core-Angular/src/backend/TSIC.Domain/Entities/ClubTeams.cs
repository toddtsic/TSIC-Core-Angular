using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ClubTeams
{
    public int ClubTeamId { get; set; }

    public int ClubId { get; set; }

    public string ClubTeamName { get; set; } = null!;

    public string ClubTeamGradYear { get; set; } = null!;

    public string ClubTeamLevelOfPlay { get; set; } = null!;

    public virtual Clubs Club { get; set; } = null!;

    public virtual ICollection<Teams> Teams { get; set; } = new List<Teams>();
}
