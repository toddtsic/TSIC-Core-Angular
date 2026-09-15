using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class OpenIddictApplications
{
    public string Id { get; set; } = null!;

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string? DisplayName { get; set; }

    public string? LogoutRedirectUri { get; set; }

    public string? RedirectUri { get; set; }

    public string? Type { get; set; }

    public virtual ICollection<OpenIddictTokens> OpenIddictTokens { get; set; } = new List<OpenIddictTokens>();
}
