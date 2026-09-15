using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class VRegistrationsSearch
{
    public Guid RegistrationId { get; set; }

    public bool? BActive { get; set; }

    public string? Registrant { get; set; }

    public string? RoleName { get; set; }

    public string? Email { get; set; }

    public string? Cellphone { get; set; }

    public DateTime? Dob { get; set; }

    public string? Assignment { get; set; }

    public decimal PaidTotal { get; set; }

    public decimal OwedTotal { get; set; }

    public DateTime RegistrationTs { get; set; }

    public string? RegistrationCategory { get; set; }
}
