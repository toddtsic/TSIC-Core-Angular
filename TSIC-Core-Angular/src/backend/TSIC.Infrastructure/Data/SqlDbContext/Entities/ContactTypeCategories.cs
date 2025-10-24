using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class ContactTypeCategories
{
    public string? ContactType { get; set; }

    public Guid ContactTypeId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }
}
