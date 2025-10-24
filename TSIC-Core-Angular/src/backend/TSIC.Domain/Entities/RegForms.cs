using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class RegForms
{
    public Guid RegFormId { get; set; }

    public Guid JobId { get; set; }

    public string FormName { get; set; } = null!;

    public string RoleIdRegistering { get; set; } = null!;

    public bool BallowMultipleRegistrations { get; set; }

    public bool AllowPif { get; set; }

    public bool ByAgeGroup { get; set; }

    public bool ByAgeRange { get; set; }

    public bool ByClubName { get; set; }

    public bool ByGradYear { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual ICollection<RegFormFields> RegFormFields { get; set; } = new List<RegFormFields>();

    public virtual ICollection<Registrations> Registrations { get; set; } = new List<Registrations>();

    public virtual AspNetRoles RoleIdRegisteringNavigation { get; set; } = null!;
}
