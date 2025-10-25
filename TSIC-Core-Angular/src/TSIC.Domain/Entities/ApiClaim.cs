using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ApiClaim
{
    public int Id { get; set; }

    public string Type { get; set; } = null!;

    public int ApiResourceId { get; set; }

    public virtual ApiResource ApiResource { get; set; } = null!;
}
