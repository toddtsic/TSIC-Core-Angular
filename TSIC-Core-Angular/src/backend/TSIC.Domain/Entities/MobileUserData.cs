using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MobileUserData
{
    public string? DataType { get; set; }

    public string? DataValue { get; set; }

    public int Id { get; set; }

    public string? UserId { get; set; }

    public virtual AspNetUsers? User { get; set; }
}
