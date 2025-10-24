using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class EmailFailures
{
    public Guid JobId { get; set; }

    public string? EmailBody { get; set; }

    public int EmailFailureId { get; set; }

    public string? EmailSubject { get; set; }

    public string? FailedEmailAddresses { get; set; }

    public string? FailureMsg { get; set; }

    public DateTime SentTs { get; set; }

    public string? SentbyUserId { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? SentbyUser { get; set; }
}
