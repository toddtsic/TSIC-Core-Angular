using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamAttendanceType
{
    public int Id { get; set; }

    public string AttendanceType { get; set; } = null!;

    public virtual ICollection<TeamAttendanceEvent> TeamAttendanceEvents { get; set; } = new List<TeamAttendanceEvent>();
}
