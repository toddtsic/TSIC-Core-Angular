using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MessageRevisions
{
    public Guid RevisionId { get; set; }

    public Guid MessageId { get; set; }

    public string PriorMessage { get; set; } = null!;

    public DateTime ReplacedAt { get; set; }

    public string EditedByUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers EditedByUser { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Messages Message { get; set; } = null!;
}
