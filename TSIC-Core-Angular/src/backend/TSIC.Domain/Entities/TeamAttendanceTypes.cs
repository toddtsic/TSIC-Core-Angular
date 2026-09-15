using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamAttendanceTypes
{
    public int Id { get; set; }

    public string AttendanceType { get; set; } = null!;

    public virtual ICollection<TeamAttendanceEvents> TeamAttendanceEvents { get; set; } = new List<TeamAttendanceEvents>();
}
