using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AspNetRoles
{
    public string? ConcurrencyStamp { get; set; }

    public string Id { get; set; } = null!;

    public string? Name { get; set; }

    public string? NormalizedName { get; set; }

    public virtual ICollection<AspNetRoleClaims> AspNetRoleClaims { get; set; } = new List<AspNetRoleClaims>();

    public virtual ICollection<JobMenus> JobMenus { get; set; } = new List<JobMenus>();

    public virtual ICollection<JobMessages> JobMessages { get; set; } = new List<JobMessages>();

    public virtual ICollection<JobWidget> JobWidget { get; set; } = new List<JobWidget>();

    public virtual ICollection<Menus> Menus { get; set; } = new List<Menus>();

    public virtual ICollection<RegForms> RegForms { get; set; } = new List<RegForms>();

    public virtual ICollection<Registrations> Registrations { get; set; } = new List<Registrations>();

    public virtual ICollection<WidgetDefault> WidgetDefault { get; set; } = new List<WidgetDefault>();

    public virtual ICollection<AspNetUsers> User { get; set; } = new List<AspNetUsers>();
}
