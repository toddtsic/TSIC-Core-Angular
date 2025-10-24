using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class PersonContacts
{
    public string UserId { get; set; } = null!;

    public bool? CcB1 { get; set; }

    public bool? CcB2 { get; set; }

    public bool? CcB3 { get; set; }

    public bool? CcB4 { get; set; }

    public string? CcCellphone { get; set; }

    public string? CcCellphoneProvider { get; set; }

    public string? CcFirstName { get; set; }

    public string? CcLastName { get; set; }

    public bool? CeB1 { get; set; }

    public bool? CeB2 { get; set; }

    public bool? CeB3 { get; set; }

    public bool? CeB4 { get; set; }

    public string? CeCellphone { get; set; }

    public string? CeCellphoneProvider { get; set; }

    public string? CeEmail { get; set; }

    public string? CeEmailSms { get; set; }

    public string? CeFirstName { get; set; }

    public string? CeHomephone { get; set; }

    public string? CeLastName { get; set; }

    public Guid? CeRelationshipId { get; set; }

    public string? CeWorkphone { get; set; }

    public bool? CpB1 { get; set; }

    public bool? CpB2 { get; set; }

    public bool? CpB3 { get; set; }

    public bool? CpB4 { get; set; }

    public string? CpCellphone { get; set; }

    public string? CpCellphoneProvider { get; set; }

    public string? CpEmail { get; set; }

    public string? CpEmailSms { get; set; }

    public string? CpFirstName { get; set; }

    public string? CpHomephone { get; set; }

    public string? CpLastName { get; set; }

    public Guid? CpRelationshipId { get; set; }

    public string? CpWorkphone { get; set; }

    public bool? CsB1 { get; set; }

    public bool? CsB2 { get; set; }

    public bool? CsB3 { get; set; }

    public bool? CsB4 { get; set; }

    public string? CsCellphone { get; set; }

    public string? CsCellphoneProvider { get; set; }

    public string? CsEmail { get; set; }

    public string? CsEmailSms { get; set; }

    public string? CsFirstName { get; set; }

    public string? CsHomephone { get; set; }

    public string? CsLastName { get; set; }

    public Guid? CsRelationshipId { get; set; }

    public string? CsWorkphone { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? REmail { get; set; }

    public string? REmailSms { get; set; }

    public virtual CellphonecarrierDomains? CeCellphoneProviderNavigation { get; set; }

    public virtual ContactRelationshipCategories? CeRelationship { get; set; }

    public virtual CellphonecarrierDomains? CpCellphoneProviderNavigation { get; set; }

    public virtual ContactRelationshipCategories? CpRelationship { get; set; }

    public virtual CellphonecarrierDomains? CsCellphoneProviderNavigation { get; set; }

    public virtual ContactRelationshipCategories? CsRelationship { get; set; }

    public virtual AspNetUsers User { get; set; } = null!;
}
