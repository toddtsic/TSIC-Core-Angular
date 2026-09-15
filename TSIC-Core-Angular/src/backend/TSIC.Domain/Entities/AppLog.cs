using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AppLog
{
    public long Id { get; set; }

    public DateTimeOffset TimeStamp { get; set; }

    public string Level { get; set; } = null!;

    public string? Message { get; set; }

    public string? Exception { get; set; }

    public string? Properties { get; set; }

    public string? SourceContext { get; set; }

    public string? RequestPath { get; set; }

    public int? StatusCode { get; set; }

    public double? Elapsed { get; set; }
}
