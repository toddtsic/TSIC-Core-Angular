using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class CustomerGroup
{
    public int Id { get; set; }

    public string CustomerGroupName { get; set; } = null!;

    public virtual ICollection<CustomerGroupCustomer> CustomerGroupCustomers { get; set; } = new List<CustomerGroupCustomer>();
}
