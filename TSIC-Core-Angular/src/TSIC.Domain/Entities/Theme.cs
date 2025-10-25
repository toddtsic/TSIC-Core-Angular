using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Theme
{
    public string Theme1 { get; set; } = null!;

    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();
}
