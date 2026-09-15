using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace TSIC.Domain.Entities;

public partial class Fields
{
    public string? Address { get; set; }

    public string? City { get; set; }

    public string? Directions { get; set; }

    public string? FName { get; set; }

    public Guid FieldId { get; set; }

    public Guid LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? State { get; set; }

    public string? Zip { get; set; }

    public Geometry? Location { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public virtual ICollection<FieldOverridesStartTimeMaxMinGames> FieldOverridesStartTimeMaxMinGames { get; set; } = new List<FieldOverridesStartTimeMaxMinGames>();

    public virtual ICollection<FieldsLeagueSeason> FieldsLeagueSeason { get; set; } = new List<FieldsLeagueSeason>();

    public virtual ICollection<Schedule> Schedule { get; set; } = new List<Schedule>();

    public virtual ICollection<Teams> TeamsFieldId1Navigation { get; set; } = new List<Teams>();

    public virtual ICollection<Teams> TeamsFieldId2Navigation { get; set; } = new List<Teams>();

    public virtual ICollection<Teams> TeamsFieldId3Navigation { get; set; } = new List<Teams>();

    public virtual ICollection<TimeslotsLeagueSeasonFields> TimeslotsLeagueSeasonFields { get; set; } = new List<TimeslotsLeagueSeasonFields>();
}
