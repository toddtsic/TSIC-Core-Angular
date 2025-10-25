using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamTournamentMapping
{
    public Guid Id { get; set; }

    public Guid ClubTeamId { get; set; }

    public Guid TournamentTeamId { get; set; }

    public virtual Team ClubTeam { get; set; } = null!;

    public virtual Team TournamentTeam { get; set; } = null!;
}
