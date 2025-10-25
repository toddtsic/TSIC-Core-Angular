using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ContactTypeCategory
{
    public string? ContactType { get; set; }

    public Guid ContactTypeId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual AspNetUser? LebUser { get; set; }
}
