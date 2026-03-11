using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class DivisionProcessingOrder
{
    public int Aid { get; set; }

    public Guid JobId { get; set; }

    public Guid DivisionId { get; set; }

    public int SortOrder { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }
}
