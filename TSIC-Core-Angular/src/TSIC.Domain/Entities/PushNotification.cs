using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PushNotification
{
    public Guid Id { get; set; }

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    public int Urgency { get; set; }

    public string JobName { get; set; } = null!;

    public string JobLogoUrl { get; set; } = null!;

    public Guid? QpJobId { get; set; }

    public Guid? QpLeagueId { get; set; }

    public Guid? QpAgegroupId { get; set; }

    public Guid? QpDivId { get; set; }

    public Guid? QpTeamId { get; set; }

    public Guid? QpRegId { get; set; }

    public string QpRole { get; set; } = null!;

    public DateTime? Created { get; set; }

    public Guid? AuthorRegistrationId { get; set; }

    public virtual Registration? AuthorRegistration { get; set; }

    public virtual Agegroup? QpAgegroup { get; set; }

    public virtual Division? QpDiv { get; set; }

    public virtual Job? QpJob { get; set; }

    public virtual League? QpLeague { get; set; }

    public virtual Registration? QpReg { get; set; }

    public virtual Team? QpTeam { get; set; }
}
