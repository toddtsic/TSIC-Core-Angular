namespace TSIC.Contracts.Dtos.Store;

/// <summary>
/// The three store email campaigns, which in legacy were three near-identical controllers
/// (<c>StoreEmailAbandondedCarts</c>, <c>StoreEmailFamiliesThatNeverUsed</c>,
/// <c>StoreEmailFamiliesThatOrdered</c>). Only the audience query and the default template differ,
/// so here they are one code path parameterized by this enum.
/// </summary>
public enum StoreCampaignKind
{
    /// <summary>Families with a cart that was touched in the age window and never paid for.</summary>
    AbandonedCarts = 0,

    /// <summary>Families registered in the job that have never opened a store cart at all.</summary>
    NeverOrdered = 1,

    /// <summary>Families that have completed at least one purchase — the pickup-instructions blast.</summary>
    HaveOrdered = 2
}

/// <summary>One abandoned cart, with the still-purchasable lines it holds.</summary>
public record StoreAbandonedCartDto
{
    public required int BatchId { get; init; }
    public required DateTime BatchDate { get; init; }
    public required string FamilyUserName { get; init; }
    public required string FamilyUserId { get; init; }

    /// <summary>
    /// One line per SKU still in stock, pre-rendered as legacy did:
    /// "2 Hoodie-Navy-YL for Jane Smith". Carts whose every line is sold out are dropped entirely.
    /// </summary>
    public required List<string> Skus { get; init; }
}

/// <summary>A substitution token offered to the composer, for the token-palette UI.</summary>
public record StoreCampaignTokenDto
{
    public required string Token { get; init; }
    public required string Label { get; init; }
}

/// <summary>
/// Everything one campaign screen needs on open: the audience size, the seeded subject/body the
/// director edits, the token palette, and — for <see cref="StoreCampaignKind.AbandonedCarts"/> —
/// the selectable cart grid and its age-window dropdowns.
/// </summary>
public record StoreCampaignSetupDto
{
    public required StoreCampaignKind Kind { get; init; }

    /// <summary>Families that would be mailed. For abandoned carts this equals <see cref="AbandonedCarts"/>.Count.</summary>
    public required int RecipientCount { get; init; }

    public required string DefaultSubject { get; init; }
    public required string DefaultBody { get; init; }
    public required List<StoreCampaignTokenDto> Tokens { get; init; }

    // ── Abandoned-carts only (empty/zero for the other two kinds) ──

    public required List<StoreAbandonedCartDto> AbandonedCarts { get; init; }
    public required int MinAgeHours { get; init; }
    public required int MaxAgeHours { get; init; }
    public required List<int> MinAgeHourOptions { get; init; }
    public required List<int> MaxAgeHourOptions { get; init; }
}

/// <summary>Compose-and-send payload. <see cref="BatchIds"/> applies only to the abandoned-carts grid.</summary>
public record StoreCampaignSendRequest
{
    public required string Subject { get; init; }
    public required string Body { get; init; }

    /// <summary>
    /// The cart batches the director ticked in the grid. Required (and non-empty) for
    /// <see cref="StoreCampaignKind.AbandonedCarts"/>; ignored by the other two kinds, whose
    /// audience is the whole computed set.
    /// </summary>
    public List<int>? BatchIds { get; init; }
}

/// <summary>Handle returned the instant the batch is accepted — sends run in the background.</summary>
public record StoreCampaignSendResponse
{
    public required Guid BatchJobId { get; init; }
    public required int TotalRecipients { get; init; }

    /// <summary>Families dropped before the batch started because no sendable address exists.</summary>
    public required int SkippedNoEmail { get; init; }
}

// ── Repository-facing shapes (not exposed on the wire) ──

/// <summary>One line of an abandoned cart, before the sold-out filter is applied.</summary>
public record StoreAbandonedCartLineDto
{
    public required int StoreSkuId { get; init; }
    public required int Quantity { get; init; }

    /// <summary>Legacy's <c>SkuQuantityNamePlayer</c>: "2 Hoodie-Navy-YL for Jane Smith".</summary>
    public required string Label { get; init; }
}

/// <summary>An abandoned cart as read from the database, lines unfiltered.</summary>
public record StoreAbandonedCartRowDto
{
    public required int BatchId { get; init; }
    public required DateTime BatchDate { get; init; }
    public required string FamilyUserName { get; init; }
    public required string FamilyUserId { get; init; }
    public required List<StoreAbandonedCartLineDto> Lines { get; init; }
}

/// <summary>
/// A campaign recipient family: its addresses, the registration whose unsubscribe link the batch
/// engine appends, and whether anyone in the family has unsubscribed.
/// </summary>
public record StoreCampaignFamilyDto
{
    public required string FamilyUserId { get; init; }
    public required string FamilyUserName { get; init; }
    public required string? MomEmail { get; init; }
    public required string? DadEmail { get; init; }

    /// <summary>
    /// Any active registration this family holds in the job — the anchor for token substitution
    /// (<c>!FAMILYUSERNAME</c>, <c>!JOBNAME</c>) and for the unsubscribe footer. Null when the
    /// family has no registration in this job (possible for a store-only walk-up family).
    /// </summary>
    public required Guid? RepresentativeRegistrationId { get; init; }

    /// <summary>
    /// True when ANY of the family's registrations carries <c>BEmailOptOut</c>. Mom and Dad share
    /// the mailbox across every child, so one unsubscribe click silences the family — the honest
    /// reading of the request, and the conservative one.
    /// </summary>
    public required bool OptedOut { get; init; }
}
