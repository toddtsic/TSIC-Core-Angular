using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class RegForm
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

    public virtual Job Job { get; set; } = null!;

    public virtual ICollection<RegFormField> RegFormFields { get; set; } = new List<RegFormField>();

    public virtual ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    public virtual AspNetRole RoleIdRegisteringNavigation { get; set; } = null!;
}
