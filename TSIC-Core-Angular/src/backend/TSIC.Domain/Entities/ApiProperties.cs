using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class ApiProperties
{
    public int Id { get; set; }

    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;

    public int ApiResourceId { get; set; }

    public virtual ApiResources ApiResource { get; set; } = null!;
}
