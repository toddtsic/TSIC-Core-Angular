using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobCustomer
{
    public Guid JobCustomerId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid JobId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual Job Job { get; set; } = null!;

    public virtual AspNetUser? LebUser { get; set; }
}
