using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MigrationHistoryOld
{
    public string MigrationId { get; set; } = null!;

    public string ContextKey { get; set; } = null!;

    public string ProductVersion { get; set; } = null!;
}
