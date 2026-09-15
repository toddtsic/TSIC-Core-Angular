using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class RegFormFieldTypes
{
    public Guid RegFormFieldTypeId { get; set; }

    public string RegFormFieldType { get; set; } = null!;

    public virtual ICollection<RegFormFields> RegFormFields { get; set; } = new List<RegFormFields>();
}
