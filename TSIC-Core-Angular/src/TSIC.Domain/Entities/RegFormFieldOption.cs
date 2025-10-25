using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class RegFormFieldOption
{
    public Guid RegFormFieldOptionId { get; set; }

    public Guid RegFormFieldId { get; set; }

    public string OptionText { get; set; } = null!;

    public string? OptionValue { get; set; }

    public int OptionRank { get; set; }

    public virtual RegFormField RegFormField { get; set; } = null!;
}
