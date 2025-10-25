using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class VMonthlyJobStats
{
    public string? CustomerName { get; set; }

    public string? JobName { get; set; }

    public int Aid { get; set; }

    public Guid JobId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public int? CountActivePlayersToDateLastMonth { get; set; }

    public int? CountActivePlayersToDate { get; set; }

    public int? CountNewPlayersThisMonth { get; set; }

    public int? CountActiveTeamsToDateLastMonth { get; set; }

    public int? CountActiveTeamsToDate { get; set; }

    public int? CountNewTeamsThisMonth { get; set; }

    public string? LastEditor { get; set; }

    public DateTime Modified { get; set; }
}
