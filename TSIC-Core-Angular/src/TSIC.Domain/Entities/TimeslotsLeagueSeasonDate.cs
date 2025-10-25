using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TimeslotsLeagueSeasonDate
{
    public int Ai { get; set; }

    public Guid AgegroupId { get; set; }

    public Guid? DivId { get; set; }

    public string Season { get; set; } = null!;

    public string Year { get; set; } = null!;

    public DateTime GDate { get; set; }

    public int Rnd { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public virtual Agegroup Agegroup { get; set; } = null!;

    public virtual Division? Div { get; set; }

    public virtual AspNetUser LebUser { get; set; } = null!;
}
