using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class EmailLog
{
    public Guid? JobId { get; set; }

    public int? Count { get; set; }

    public int EmailId { get; set; }

    public string? Msg { get; set; }

    public string? SendFrom { get; set; }

    public DateTime SendTs { get; set; }

    public string? SendTo { get; set; }

    public string? SenderUserId { get; set; }

    public string? Subject { get; set; }

    public virtual Job? Job { get; set; }

    public virtual AspNetUser? SenderUser { get; set; }
}
