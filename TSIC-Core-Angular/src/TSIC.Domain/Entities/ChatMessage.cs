using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ChatMessage
{
    public Guid MessageId { get; set; }

    public string? Message { get; set; }

    public Guid TeamId { get; set; }

    public string CreatorUserId { get; set; } = null!;

    public DateTime Created { get; set; }

    public virtual AspNetUser CreatorUser { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
