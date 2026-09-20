using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class InviteStatuses
{
    public int InviteStatusId { get; set; }

    public string InviteStatusName { get; set; } = null!;
}
