using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class CustomerGroupCustomers
{
    public int Id { get; set; }

    public int CustomerGroupId { get; set; }

    public Guid CustomerId { get; set; }

    public virtual Customers Customer { get; set; } = null!;

    public virtual CustomerGroups CustomerGroup { get; set; } = null!;
}
