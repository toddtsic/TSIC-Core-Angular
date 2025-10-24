using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class ApiRosterPlayersAccessed
{
    public Guid Id { get; set; }

    public string ApiUserId { get; set; } = null!;

    public Guid RegistrationId { get; set; }

    public DateTime WhenAccessed { get; set; }

    public virtual AspNetUsers ApiUser { get; set; } = null!;

    public virtual Registrations Registration { get; set; } = null!;
}
