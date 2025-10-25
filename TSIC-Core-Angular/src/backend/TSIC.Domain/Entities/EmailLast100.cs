using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class EmailLast100
{
    public string? CustomerName { get; set; }

    public string? JobName { get; set; }

    public Guid? JobId { get; set; }

    public int? Count { get; set; }

    public int EmailId { get; set; }

    public string? Msg { get; set; }

    public string? SendFrom { get; set; }

    public DateTime SendTs { get; set; }

    public string? SendTo { get; set; }

    public string? SenderUserId { get; set; }

    public string? Subject { get; set; }
}
