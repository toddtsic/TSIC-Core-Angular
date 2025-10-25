using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Ftext
{
    public bool Active { get; set; }

    public DateTime CreateDate { get; set; }

    public string? LebUserId { get; set; }

    public Guid MenuItemId { get; set; }

    public DateTime Modified { get; set; }

    public string? Text { get; set; }

    public virtual AspNetUser? LebUser { get; set; }
}
