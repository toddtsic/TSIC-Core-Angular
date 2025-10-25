using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class RegistrationFormPaymentMethods
{
    public string? RegistrationFormPaymentMethod { get; set; }

    public Guid RegistrationFormPaymentMethodId { get; set; }
}
