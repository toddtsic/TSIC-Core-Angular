using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Events
{
    public Guid EventId { get; set; }

    public Guid TeamId { get; set; }

    public Guid JobId { get; set; }

    public byte EventType { get; set; }

    public string Title { get; set; } = null!;

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public DateTime? ArriveAt { get; set; }

    public bool IsAllDay { get; set; }

    public string? TimeZoneId { get; set; }

    public string? Location { get; set; }

    public string? LocationUrl { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? Notes { get; set; }

    public string? Opponent { get; set; }

    public byte? HomeAway { get; set; }

    public string? Uniform { get; set; }

    public short? ScoreUs { get; set; }

    public short? ScoreThem { get; set; }

    public string? ResultNote { get; set; }

    public byte Status { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancelledByUserId { get; set; }

    public string? CancelReason { get; set; }

    public bool AvailabilityEnabled { get; set; }

    public int Revision { get; set; }

    public Guid? SeriesId { get; set; }

    public byte Source { get; set; }

    public string? ExternalCalendarId { get; set; }

    public string? ExternalEventId { get; set; }

    public DateTime? ExternalUpdated { get; set; }

    public DateTime Created { get; set; }

    public string? CreatorUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual ICollection<Attendance> Attendance { get; set; } = new List<Attendance>();

    public virtual ICollection<Availability> Availability { get; set; } = new List<Availability>();

    public virtual AspNetUsers? CancelledByUser { get; set; }

    public virtual AspNetUsers? CreatorUser { get; set; }

    public virtual ICollection<Duties> Duties { get; set; } = new List<Duties>();

    public virtual ICollection<EventRevisions> EventRevisions { get; set; } = new List<EventRevisions>();

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<Lineups> Lineups { get; set; } = new List<Lineups>();

    public virtual ICollection<Reminders> Reminders { get; set; } = new List<Reminders>();

    public virtual Series? Series { get; set; }

    public virtual ICollection<Stats> Stats { get; set; } = new List<Stats>();

    public virtual Teams Team { get; set; } = null!;
}
