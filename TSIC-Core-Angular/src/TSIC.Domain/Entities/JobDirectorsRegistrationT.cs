using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobDirectorsRegistrationT
{
    public string? Director { get; set; }

    public DateTime RegistrationTs { get; set; }

    public string? RoleId { get; set; }
}
