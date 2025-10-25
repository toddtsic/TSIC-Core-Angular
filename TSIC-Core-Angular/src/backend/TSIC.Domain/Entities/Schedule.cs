using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Schedule
{
    public int Gid { get; set; }

    public Guid LeagueId { get; set; }

    public string? LeagueName { get; set; }

    public string Season { get; set; } = null!;

    public string Year { get; set; } = null!;

    public Guid? AgegroupId { get; set; }

    public string? AgegroupName { get; set; }

    public Guid? DivId { get; set; }

    public string? DivName { get; set; }

    public Guid? Div2Id { get; set; }

    public string? Div2Name { get; set; }

    public int? GNo { get; set; }

    public DateTime? GDate { get; set; }

    public int? GStatusCode { get; set; }

    public byte? Rnd { get; set; }

    public Guid? FieldId { get; set; }

    public string? T1Name { get; set; }

    public string? T1Type { get; set; }

    public int? T1No { get; set; }

    public Guid? T1Id { get; set; }

    public int? T1Score { get; set; }

    public int? T1penalties { get; set; }

    public int? T1GnoRef { get; set; }

    public string? T1CalcType { get; set; }

    public string? T2Name { get; set; }

    public string? T2Type { get; set; }

    public byte? T2No { get; set; }

    public Guid? T2Id { get; set; }

    public int? T2Score { get; set; }

    public int? T2penalties { get; set; }

    public int? T2GnoRef { get; set; }

    public string? T1Ann { get; set; }

    public string? T2Ann { get; set; }

    public string? T2CalcType { get; set; }

    public decimal? RefCount { get; set; }

    public int? RescheduleCount { get; set; }

    public DateTime? Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public string? FName { get; set; }

    public Guid JobId { get; set; }

    public virtual Agegroups? Agegroup { get; set; }

    public virtual ICollection<BracketSeeds> BracketSeeds { get; set; } = new List<BracketSeeds>();

    public virtual ICollection<DeviceGids> DeviceGids { get; set; } = new List<DeviceGids>();

    public virtual Divisions? Div { get; set; }

    public virtual Divisions? Div2 { get; set; }

    public virtual Fields? Field { get; set; }

    public virtual GameStatusCodes? GStatusCodeNavigation { get; set; }

    public virtual ICollection<Schedule> InverseT1GnoRefNavigation { get; set; } = new List<Schedule>();

    public virtual ICollection<Schedule> InverseT2GnoRefNavigation { get; set; } = new List<Schedule>();

    public virtual Jobs Job { get; set; } = null!;

    public virtual Leagues League { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual ICollection<RefGameAssigments> RefGameAssigments { get; set; } = new List<RefGameAssigments>();

    public virtual Teams? T1 { get; set; }

    public virtual Schedule? T1GnoRefNavigation { get; set; }

    public virtual ScheduleTeamTypes? T1TypeNavigation { get; set; }

    public virtual Teams? T2 { get; set; }

    public virtual Schedule? T2GnoRefNavigation { get; set; }

    public virtual ScheduleTeamTypes? T2TypeNavigation { get; set; }
}
