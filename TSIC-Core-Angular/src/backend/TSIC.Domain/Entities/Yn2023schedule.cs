using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class Yn2023schedule
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
}
