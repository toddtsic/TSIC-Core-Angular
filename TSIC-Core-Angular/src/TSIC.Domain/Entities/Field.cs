using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace TSIC.Domain.Entities;

public partial class Field
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

    public virtual ICollection<FieldOverridesStartTimeMaxMinGame> FieldOverridesStartTimeMaxMinGames { get; set; } = new List<FieldOverridesStartTimeMaxMinGame>();

    public virtual ICollection<FieldsLeagueSeason> FieldsLeagueSeasons { get; set; } = new List<FieldsLeagueSeason>();

    public virtual ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();

    public virtual ICollection<Team> TeamFieldId1Navigations { get; set; } = new List<Team>();

    public virtual ICollection<Team> TeamFieldId2Navigations { get; set; } = new List<Team>();

    public virtual ICollection<Team> TeamFieldId3Navigations { get; set; } = new List<Team>();

    public virtual ICollection<TimeslotsLeagueSeasonField> TimeslotsLeagueSeasonFields { get; set; } = new List<TimeslotsLeagueSeasonField>();
}
