using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class RegFormFieldType
{
    public Guid RegFormFieldTypeId { get; set; }

    public string RegFormFieldType1 { get; set; } = null!;

    public virtual ICollection<RegFormField> RegFormFields { get; set; } = new List<RegFormField>();
}
