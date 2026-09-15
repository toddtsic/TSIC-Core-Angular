using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class SweepLog
{
    public long SweepLogId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string TriggeredBy { get; set; } = null!;

    public int RecordsChecked { get; set; }

    public int RecordsSettled { get; set; }

    public int RecordsReturned { get; set; }

    public int RecordsErrored { get; set; }

    public string? ErrorMessage { get; set; }
}
