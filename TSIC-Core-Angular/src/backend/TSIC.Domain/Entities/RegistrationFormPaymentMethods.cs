using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class RegistrationFormPaymentMethods
{
    public string? RegistrationFormPaymentMethod { get; set; }

    public Guid RegistrationFormPaymentMethodId { get; set; }
}
