using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class RegFormFieldOptions
{
    public Guid RegFormFieldOptionId { get; set; }

    public Guid RegFormFieldId { get; set; }

    public string OptionText { get; set; } = null!;

    public string? OptionValue { get; set; }

    public int OptionRank { get; set; }

    public virtual RegFormFields RegFormField { get; set; } = null!;
}
