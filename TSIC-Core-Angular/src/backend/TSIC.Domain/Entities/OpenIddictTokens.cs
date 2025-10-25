using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class OpenIddictTokens
{
    public string Id { get; set; } = null!;

    public string? ApplicationId { get; set; }

    public string? AuthorizationId { get; set; }

    public string? Type { get; set; }

    public string? Subject { get; set; }

    public virtual OpenIddictApplications? Application { get; set; }

    public virtual OpenIddictAuthorizations? Authorization { get; set; }
}
