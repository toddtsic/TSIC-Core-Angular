using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class Themes
{
    public string Theme { get; set; } = null!;

    public virtual ICollection<Customers> Customers { get; set; } = new List<Customers>();
}
