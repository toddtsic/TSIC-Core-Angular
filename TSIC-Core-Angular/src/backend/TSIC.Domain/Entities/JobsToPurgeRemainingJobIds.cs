using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class JobsToPurgeRemainingJobIds
{
    public Guid JobId { get; set; }
}
