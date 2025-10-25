using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class EmailFailure
{
    public Guid JobId { get; set; }

    public string? EmailBody { get; set; }

    public int EmailFailureId { get; set; }

    public string? EmailSubject { get; set; }

    public string? FailedEmailAddresses { get; set; }

    public string? FailureMsg { get; set; }

    public DateTime SentTs { get; set; }

    public string? SentbyUserId { get; set; }

    public virtual Job Job { get; set; } = null!;

    public virtual AspNetUser? SentbyUser { get; set; }
}
