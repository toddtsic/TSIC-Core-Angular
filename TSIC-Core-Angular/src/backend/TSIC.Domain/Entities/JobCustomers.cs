using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobCustomers
{
    public Guid JobCustomerId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid JobId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual Customers Customer { get; set; } = null!;

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
