using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class TeamTournamentMappings
{
    public Guid Id { get; set; }

    public Guid ClubTeamId { get; set; }

    public Guid TournamentTeamId { get; set; }

    public virtual Teams ClubTeam { get; set; } = null!;

    public virtual Teams TournamentTeam { get; set; } = null!;
}
