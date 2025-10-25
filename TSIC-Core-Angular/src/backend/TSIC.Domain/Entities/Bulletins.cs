using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Bulletins
{
    public int ExpireHours { get; set; }

    public bool Active { get; set; }

    public Guid BulletinId { get; set; }

    public DateTime CreateDate { get; set; }

    public Guid JobId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? Text { get; set; }

    public string? Title { get; set; }

    public bool? Bcore { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
