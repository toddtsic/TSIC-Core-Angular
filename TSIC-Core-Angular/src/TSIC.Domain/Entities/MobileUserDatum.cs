using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MobileUserDatum
{
    public string? DataType { get; set; }

    public string? DataValue { get; set; }

    public int Id { get; set; }

    public string? UserId { get; set; }

    public virtual AspNetUser? User { get; set; }
}
