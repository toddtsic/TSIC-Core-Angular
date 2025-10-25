using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class FieldOverridesStartTimeMaxMinGame
{
    public int Ai { get; set; }

    public Guid? LeagueId { get; set; }

    public string? Season { get; set; }

    public string? Year { get; set; }

    public Guid? FieldId { get; set; }

    public string? StartTime { get; set; }

    public int? MinGamesPerField { get; set; }

    public int? MaxGamesPerField { get; set; }

    public string? Dow { get; set; }

    public virtual Field? Field { get; set; }

    public virtual League? League { get; set; }
}
