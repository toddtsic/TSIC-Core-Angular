using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class Ftexts
{
    public bool Active { get; set; }

    public DateTime CreateDate { get; set; }

    public string? LebUserId { get; set; }

    public Guid MenuItemId { get; set; }

    public DateTime Modified { get; set; }

    public string? Text { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }
}
