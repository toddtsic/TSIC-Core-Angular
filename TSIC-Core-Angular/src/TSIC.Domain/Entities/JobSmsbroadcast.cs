using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobSmsbroadcast
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string Smsbody { get; set; } = null!;

    public string Promotion { get; set; } = null!;

    public string SendRequestId { get; set; } = null!;

    public int CellphoneCount { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual AspNetUser LebUser { get; set; } = null!;
}
