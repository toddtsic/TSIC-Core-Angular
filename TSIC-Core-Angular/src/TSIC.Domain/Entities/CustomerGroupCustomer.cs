using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class CustomerGroupCustomer
{
    public int Id { get; set; }

    public int CustomerGroupId { get; set; }

    public Guid CustomerId { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual CustomerGroup CustomerGroup { get; set; } = null!;
}
