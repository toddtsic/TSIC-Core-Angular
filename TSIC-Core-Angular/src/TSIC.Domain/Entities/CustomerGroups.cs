using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class CustomerGroups
{
    public int Id { get; set; }

    public string CustomerGroupName { get; set; } = null!;

    public virtual ICollection<CustomerGroupCustomers> CustomerGroupCustomers { get; set; } = new List<CustomerGroupCustomers>();
}
