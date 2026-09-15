using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TimeslotsLeagueSeasonFields
{
    public int Ai { get; set; }

    public Guid AgegroupId { get; set; }

    public string Season { get; set; } = null!;

    public string Year { get; set; } = null!;

    public Guid FieldId { get; set; }

    public string? StartTime { get; set; }

    public int GamestartInterval { get; set; }

    public int MaxGamesPerField { get; set; }

    public string Dow { get; set; } = null!;

    public Guid? DivId { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public virtual Agegroups Agegroup { get; set; } = null!;

    public virtual Divisions? Div { get; set; }

    public virtual Fields Field { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;
}
