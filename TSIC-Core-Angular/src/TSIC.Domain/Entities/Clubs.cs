using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Clubs
{
    public Guid ClubId { get; set; }

    public string? ClubName { get; set; }

    public string? DirectorEmail { get; set; }

    public string? DirectorName { get; set; }

    public string? DirectorPhone1 { get; set; }

    public string? DirectorPhone2 { get; set; }

    public string? DirectorPhone3 { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? PresEmail { get; set; }

    public string? PresName { get; set; }

    public string? PresPhone1 { get; set; }

    public string? PresPhone2 { get; set; }

    public string? PresPhone3 { get; set; }

    public string? RepEmail { get; set; }

    public string? RepName { get; set; }

    public string? RepPhone1 { get; set; }

    public string? RepPhone2 { get; set; }

    public string? RepPhone3 { get; set; }

    public string? SchedulerEmail { get; set; }

    public string? SchedulerName { get; set; }

    public string? SchedulerPhone1 { get; set; }

    public string? SchedulerPhone2 { get; set; }

    public string? SchedulerPhone3 { get; set; }

    public Guid SportId { get; set; }

    public string? UrlWebsite { get; set; }

    public string? UrlWebsiteLogo { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Sports Sport { get; set; } = null!;
}
