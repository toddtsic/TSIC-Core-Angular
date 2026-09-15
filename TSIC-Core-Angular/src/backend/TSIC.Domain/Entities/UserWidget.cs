using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class UserWidget
{
    public int UserWidgetId { get; set; }

    public Guid RegistrationId { get; set; }

    public int WidgetId { get; set; }

    public int CategoryId { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsHidden { get; set; }

    public string? Config { get; set; }

    public virtual WidgetCategory Category { get; set; } = null!;

    public virtual Widget Widget { get; set; } = null!;
}
