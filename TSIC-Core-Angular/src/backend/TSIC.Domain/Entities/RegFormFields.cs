using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class RegFormFields
{
    public Guid RegFormFieldId { get; set; }

    public Guid RegFormId { get; set; }

    public Guid RegFormFieldTypeId { get; set; }

    public bool Active { get; set; }

    public int FieldRank { get; set; }

    public string? FieldName { get; set; }

    public string FieldLabel { get; set; } = null!;

    public bool BadminOnly { get; set; }

    public bool IsDisabled { get; set; }

    public bool BremoteValidation { get; set; }

    public bool ValidatorIsRequired { get; set; }

    public bool ValidatorMustBeTrue { get; set; }

    public string? ValidatorRegEx { get; set; }

    public decimal? ValidatorRangeMin { get; set; }

    public decimal? ValidatorRangeMax { get; set; }

    public string? ValidatorIsRequiredErrorMessage { get; set; }

    public string? ValidatorMustBeTrueErrorMessage { get; set; }

    public string? ValidatorRegExErrorMessage { get; set; }

    public string? ValidatorRangeErrorMessage { get; set; }

    public string? FieldHint { get; set; }

    public virtual RegForms RegForm { get; set; } = null!;

    public virtual ICollection<RegFormFieldOptions> RegFormFieldOptions { get; set; } = new List<RegFormFieldOptions>();

    public virtual RegFormFieldTypes RegFormFieldType { get; set; } = null!;
}
