using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamDoc
{
    public Guid DocId { get; set; }

    public Guid? TeamId { get; set; }

    public Guid? JobId { get; set; }

    public string Label { get; set; } = null!;

    public DateTime CreateDate { get; set; }

    public string UserId { get; set; } = null!;

    public string DocUrl { get; set; } = null!;

    public virtual Job? Job { get; set; }

    public virtual Team? Team { get; set; }

    public virtual AspNetUser User { get; set; } = null!;
}
