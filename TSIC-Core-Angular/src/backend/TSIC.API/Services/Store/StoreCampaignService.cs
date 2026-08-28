using TSIC.API.Configuration;
using TSIC.API.Services.Shared.Email;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Store;

/// <inheritdoc cref="IStoreCampaignService"/>
public sealed class StoreCampaignService : IStoreCampaignService
{
    private readonly IStoreRepository _storeRepo;
    private readonly IStoreCampaignRepository _campaignRepo;
    private readonly IStoreCartService _cartService;
    private readonly IJobRepository _jobRepo;
    private readonly IUserRepository _userRepo;
    private readonly IEmailBatchService _emailBatch;
    private readonly string _frontendBaseUrl;

    /// <summary>Payment-method id the substitution engine uses to classify credit-card accounting rows.</summary>
    private static readonly Guid CcPaymentMethodId = new("30ECA575-A268-E111-9D56-F04DA202060D");

    // Legacy's dropdown ranges, verbatim: min 0..12 by 1, max 24..48 by 6.
    private const int DefaultMinAgeHours = 6;
    private const int DefaultMaxAgeHours = 24;
    private static readonly List<int> MinAgeOptions = Enumerable.Range(0, 13).ToList();
    private static readonly List<int> MaxAgeOptions = [24, 30, 36, 42, 48];

    /// <summary>
    /// Store-specific tokens the shared substitution engine does not produce. They are merged in as
    /// extraTokens; the engine throws if an extra key collides with one it owns, which is what keeps
    /// this from quietly shadowing a real token.
    /// </summary>
    private const string StoreLinkToken = "!STORELINK";
    private const string CartSkusToken = "!BATCHCARTSKUS";

    public StoreCampaignService(
        IStoreRepository storeRepo,
        IStoreCampaignRepository campaignRepo,
        IStoreCartService cartService,
        IJobRepository jobRepo,
        IUserRepository userRepo,
        IEmailBatchService emailBatch,
        Microsoft.Extensions.Options.IOptions<FrontendSettings> frontendSettings)
    {
        _storeRepo = storeRepo;
        _campaignRepo = campaignRepo;
        _cartService = cartService;
        _jobRepo = jobRepo;
        _userRepo = userRepo;
        _emailBatch = emailBatch;
        _frontendBaseUrl = (frontendSettings.Value.BaseUrl ?? string.Empty).TrimEnd('/');
    }

    // ═══════════════════════════════════════════
    //  SETUP
    // ═══════════════════════════════════════════

    public async Task<StoreCampaignSetupDto> GetSetupAsync(
        Guid jobId,
        StoreCampaignKind kind,
        int? minAgeHours = null,
        int? maxAgeHours = null,
        CancellationToken cancellationToken = default)
    {
        var storeId = await ResolveStoreIdAsync(jobId, cancellationToken);
        var jobName = await _jobRepo.GetJobNameAsync(jobId, cancellationToken) ?? string.Empty;

        var min = ClampMin(minAgeHours ?? DefaultMinAgeHours);
        var max = ClampMax(maxAgeHours ?? DefaultMaxAgeHours);

        var carts = kind == StoreCampaignKind.AbandonedCarts
            ? await LoadPurchasableAbandonedCartsAsync(storeId, min, max, cancellationToken)
            : [];

        // Count the families this campaign would actually REACH, not the raw audience size —
        // resolved through the same path the send uses, so "27 families" and "sent 27" agree.
        // Legacy counted ids and then silently dropped the address-less ones mid-send.
        var recipientCount = kind == StoreCampaignKind.AbandonedCarts
            ? carts.Count
            : (await ResolveFixedAudienceAsync(jobId, storeId, kind, cancellationToken))
                .Count(r => r.ToAddresses.Count > 0);

        return new StoreCampaignSetupDto
        {
            Kind = kind,
            RecipientCount = recipientCount,
            DefaultSubject = DefaultSubject(kind, jobName),
            DefaultBody = DefaultBody(kind),
            Tokens = TokenPalette(kind),
            AbandonedCarts = carts,
            MinAgeHours = min,
            MaxAgeHours = max,
            MinAgeHourOptions = MinAgeOptions,
            MaxAgeHourOptions = MaxAgeOptions
        };
    }

    /// <summary>
    /// Abandoned carts, minus lines that can no longer be fulfilled — and minus carts left with
    /// nothing at all. Advertising a sold-out item back to a family is worse than staying quiet.
    ///
    /// The stock test is legacy's: MaxCanSell − Sold. It deliberately IGNORES what sits in carts,
    /// because the cart being advertised is itself one of them — netting in-cart quantities here
    /// would zero out every abandoned cart and the campaign would find nobody to mail.
    /// </summary>
    private async Task<List<StoreAbandonedCartDto>> LoadPurchasableAbandonedCartsAsync(
        int storeId, int minAgeHours, int maxAgeHours, CancellationToken ct)
    {
        var rows = await _campaignRepo.GetAbandonedCartsAsync(storeId, minAgeHours, maxAgeHours, ct);
        if (rows.Count == 0) return [];

        var skuIds = rows.SelectMany(r => r.Lines).Select(l => l.StoreSkuId).Distinct().ToList();
        var availability = await _cartService.CheckAvailabilityBatchAsync(skuIds);
        var stockBySku = availability.ToDictionary(a => a.StoreSkuId, a => a.MaxCanSell - a.SoldCount);

        return rows
            .Select(r => new StoreAbandonedCartDto
            {
                BatchId = r.BatchId,
                BatchDate = r.BatchDate,
                FamilyUserName = r.FamilyUserName,
                FamilyUserId = r.FamilyUserId,
                Skus = r.Lines
                    .Where(l => stockBySku.GetValueOrDefault(l.StoreSkuId, 0) > 0)
                    .Select(l => l.Label)
                    .ToList()
            })
            .Where(c => c.Skus.Count > 0)
            .ToList();
    }

    // ═══════════════════════════════════════════
    //  SEND
    // ═══════════════════════════════════════════

    public async Task<StoreCampaignSendResponse> SendAsync(
        Guid jobId,
        string senderUserId,
        StoreCampaignKind kind,
        StoreCampaignSendRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
            throw new InvalidOperationException("The subject and email body are both required.");

        var storeId = await ResolveStoreIdAsync(jobId, cancellationToken);

        var recipients = await ResolveAudienceAsync(jobId, storeId, kind, request, cancellationToken);

        // Families with no usable address never enter the batch — they would only ever be counted as
        // failures, and the director's real question is "how many did this actually reach".
        var skippedNoEmail = recipients.Count(r => r.ToAddresses.Count == 0);
        recipients = recipients.Where(r => r.ToAddresses.Count > 0).ToList();

        if (recipients.Count == 0)
            throw new InvalidOperationException("No recipients with a usable email address.");

        var jobInfo = await _jobRepo.GetConfirmationEmailInfoAsync(jobId, cancellationToken);
        var jobPath = jobInfo?.JobPath ?? string.Empty;
        var fromName = jobInfo?.DisplayName ?? jobInfo?.JobName;

        // Reply-To = the admin who fired the campaign. The From address is forced to the SES-verified
        // identity downstream, so the sender's identity lives solely on Reply-To.
        var sender = await _userRepo.GetByIdAsync(senderUserId, cancellationToken);
        var replyToAddress = sender?.Email;
        var replyToName = $"{sender?.FirstName} {sender?.LastName}".Trim();

        var storeLink = BuildStoreLink(jobPath);
        var jobName = jobInfo?.JobName ?? string.Empty;
        var jobLink = BuildJobLink(jobPath, jobName);
        var subject = request.Subject;
        var body = request.Body;

        var plan = new EmailBatchPlan<StoreCampaignRecipient>
        {
            SeedAsync = (_, _) => Task.FromResult(new EmailBatchSeed<StoreCampaignRecipient> { Items = recipients }),

            // Legacy honored no opt-out at all on these three screens — a family that had clicked
            // unsubscribe still got store blasts. The engine applies this uniformly for every batch
            // path, which is the whole point of it living there and not in each caller.
            IsOptedOut = r => r.OptedOut,

            DescribeItem = r => r.FamilyUserName,

            RenderAsync = async (r, sp, _) =>
            {
                var textSub = sp.GetRequiredService<ITextSubstitutionService>();

                var extras = new Dictionary<string, string>
                {
                    [StoreLinkToken] = storeLink,
                    [CartSkusToken] = r.CartSkusHtml
                };

                // A store family need not be registered in the job — on the reference walk-up store,
                // 24 of 27 purchasing families are not. The substitution engine keys entirely off
                // Registrations (same jobId + familyUserId predicate that produced this anchor), so
                // with no anchor it produces NO tokens and the templates would ship literal
                // "!JOBNAME" text. Supply the handful the store templates need. Safe against the
                // engine's extras-collision guard precisely because a null anchor means the engine
                // emitted nothing to collide with.
                if (r.RepresentativeRegistrationId == null)
                {
                    extras["!JOBNAME"] = jobName;
                    extras["!FAMILYUSERNAME"] = r.FamilyUserName;
                    extras["!JOBLINK"] = jobLink;
                }

                var (renderedSubject, renderedBody) = await textSub.SubstituteSubjectAndBodyAsync(
                    jobPath, jobId, CcPaymentMethodId,
                    r.RepresentativeRegistrationId, r.FamilyUserId,
                    subject, body,
                    extraTokens: extras, emailMode: true);

                return new EmailBatchRendered
                {
                    Message = new EmailMessageDto
                    {
                        FromName = fromName,
                        ReplyToName = replyToName,
                        ReplyToAddress = replyToAddress,
                        Subject = renderedSubject,
                        HtmlBody = renderedBody,
                        ToAddresses = r.ToAddresses
                    },
                    // No registration anchor means no unsubscribe link the engine can build — a
                    // store-only family has nothing in Registrations to suppress.
                    UnsubscribeRegId = r.RepresentativeRegistrationId
                };
            },

            Audit = new EmailBatchAudit
            {
                JobId = jobId,
                SenderUserId = senderUserId,
                Subject = subject,
                BodyTemplate = body,
                SendFrom = replyToAddress
            },

            // Legacy mailed the sender a hand-rolled confirmation from inside each of the three
            // controllers. Same receipt, one implementation, and it now also reaches the job's
            // always-copy oversight list like every other blast.
            OnCompleteAsync = (status, sp, token) => BatchCompletionReceipt.SendAsync(
                status, sp, jobId, replyToAddress, fromName, subject, body, token)
        };

        var handle = await _emailBatch.StartAsync(plan, new EmailBatchOptions(), cancellationToken);

        return new StoreCampaignSendResponse
        {
            BatchJobId = handle.JobId,
            TotalRecipients = handle.TotalRecipients,
            SkippedNoEmail = skippedNoEmail
        };
    }

    // ═══════════════════════════════════════════
    //  AUDIENCE
    // ═══════════════════════════════════════════

    /// <summary>
    /// The ONE thing that differs between the three campaigns. Everything downstream — addresses,
    /// opt-out, render, audit, receipt — is shared.
    /// </summary>
    private async Task<List<StoreCampaignRecipient>> ResolveAudienceAsync(
        Guid jobId, int storeId, StoreCampaignKind kind, StoreCampaignSendRequest request, CancellationToken ct)
    {
        if (kind == StoreCampaignKind.AbandonedCarts)
        {
            if (request.BatchIds is not { Count: > 0 })
                throw new InvalidOperationException("No carts were selected.");

            // Re-resolve the carts server-side rather than trusting the posted rows: the client sends
            // ids, and the SKU list it was shown may be minutes stale. Selecting an id outside the
            // current window simply does not match — it cannot address a cart in another store.
            var carts = await LoadPurchasableAbandonedCartsAsync(storeId, 0, int.MaxValue, ct);
            var selected = carts.Where(c => request.BatchIds.Contains(c.BatchId)).ToList();

            if (selected.Count == 0)
                throw new InvalidOperationException("None of the selected carts are still eligible.");

            var contacts = await _campaignRepo.GetFamilyContactsAsync(
                jobId, selected.Select(c => c.FamilyUserId).ToList(), ct);
            var contactsByFamily = contacts.ToDictionary(c => c.FamilyUserId, StringComparer.OrdinalIgnoreCase);

            // One message per CART, not per family: a family with two abandoned carts gets one email
            // per cart, each listing its own contents. That is legacy's shape and the useful one.
            return selected
                .Select(c => contactsByFamily.TryGetValue(c.FamilyUserId, out var contact)
                    ? BuildRecipient(contact, BuildSkuListHtml(c.Skus))
                    : null)
                .Where(r => r != null)
                .Select(r => r!)
                .ToList();
        }

        return await ResolveFixedAudienceAsync(jobId, storeId, kind, ct);
    }

    /// <summary>The two whole-audience campaigns. Shared by the setup headcount and the send.</summary>
    private async Task<List<StoreCampaignRecipient>> ResolveFixedAudienceAsync(
        Guid jobId, int storeId, StoreCampaignKind kind, CancellationToken ct)
    {
        var familyIds = kind == StoreCampaignKind.NeverOrdered
            ? await _campaignRepo.GetFamilyUserIdsNeverOrderedAsync(jobId, storeId, ct)
            : await _campaignRepo.GetFamilyUserIdsThatOrderedAsync(storeId, ct);

        var families = await _campaignRepo.GetFamilyContactsAsync(jobId, familyIds, ct);
        return families.Select(f => BuildRecipient(f, string.Empty)).ToList();
    }

    private static StoreCampaignRecipient BuildRecipient(StoreCampaignFamilyDto family, string cartSkusHtml) =>
        new()
        {
            FamilyUserId = family.FamilyUserId,
            FamilyUserName = family.FamilyUserName,
            RepresentativeRegistrationId = family.RepresentativeRegistrationId,
            OptedOut = family.OptedOut,
            // Mom + Dad, sentinel/invalid stripped and de-duplicated by the shared filter — the same
            // rule every other batch path uses, replacing legacy's inline Replace("not@given.com","")
            // plus a case-insensitive mom-vs-dad comparison.
            ToAddresses = BatchEmailRecipientFilter.BuildSendableSet([family.MomEmail, family.DadEmail]),
            CartSkusHtml = cartSkusHtml
        };

    /// <summary>
    /// Renders <c>!BATCHCARTSKUS</c>. Legacy opened a &lt;ul&gt; and appended the raw strings without
    /// &lt;li&gt; wrappers, so the list rendered as one run-on line. The items are wrapped here.
    /// </summary>
    private static string BuildSkuListHtml(IEnumerable<string> skus)
    {
        var items = string.Concat(skus.Select(s => $"<li>{System.Net.WebUtility.HtmlEncode(s)}</li>"));
        return string.IsNullOrEmpty(items) ? string.Empty : $"<ul>{items}</ul>";
    }

    /// <summary>Same shape the substitution engine renders for <c>!JOBLINK</c>.</summary>
    private string BuildJobLink(string jobPath, string jobName) =>
        string.IsNullOrEmpty(jobPath)
            ? string.Empty
            : $"<a href='{_frontendBaseUrl}/{jobPath}' target='_blank'>{System.Net.WebUtility.HtmlEncode(jobName)}</a>";

    private string BuildStoreLink(string jobPath) =>
        string.IsNullOrEmpty(jobPath)
            ? string.Empty
            : $"{_frontendBaseUrl}/{jobPath}/store/login";

    // ═══════════════════════════════════════════
    //  TEMPLATES
    // ═══════════════════════════════════════════

    private static string DefaultSubject(StoreCampaignKind kind, string jobName) => kind switch
    {
        StoreCampaignKind.HaveOrdered => $"{jobName}: Pickup Instructions",
        _ => $"{jobName}: Shopping Carts"
    };

    /// <summary>Legacy's seeded editor content, verbatim apart from the store-link token rename.</summary>
    private static string DefaultBody(StoreCampaignKind kind) => kind switch
    {
        StoreCampaignKind.AbandonedCarts =>
            """
            <p>Don't miss out on !JOBNAME MERCH!</p>
            <p>We noticed that you left some items in your cart. We wanted to remind you that they are still waiting for you and we would love for you to complete your purchase.</p>
            <p>Here's a quick reminder of what you left behind</p>
            <p>!BATCHCARTSKUS</p>
            <p>To complete your purchase</p>
            <ul>
                <li>Go to: <a href="!STORELINK">!STORELINK</a></li>
                <li>Login with your Family UserName: !FAMILYUSERNAME</li>
                <li>Select "Shopping Cart"</li>
            </ul>
            <p>Happy Shopping!</p>
            """,

        StoreCampaignKind.NeverOrdered =>
            """
            <p>Don't miss out on !JOBNAME MERCH!</p>
            <p>Check out the tournament merchandise available for pre-ordering online.</p>
            <p>To place your order</p>
            <ul>
                <li>Go to: <a href="!STORELINK">!STORELINK</a></li>
                <li>Login with your Family UserName: !FAMILYUSERNAME</li>
            </ul>
            <p>Happy Shopping!</p>
            """,

        _ =>
            """
            <p>We're looking forward to seeing you at !JOBNAME.</p>
            <p>To pick up your pre-purchased merchandise:</p>
            <p><a href="!STORELINK">Click here</a> to look up your receipt.</p>
            <p>Thank You!</p>
            """
    };

    private static List<StoreCampaignTokenDto> TokenPalette(StoreCampaignKind kind)
    {
        var tokens = new List<StoreCampaignTokenDto>
        {
            new() { Token = "!FAMILYUSERNAME", Label = "Family username" },
            new() { Token = StoreLinkToken, Label = "Link to this job's store" },
            new() { Token = "!JOBLINK", Label = "Job name as a clickable link" },
            new() { Token = "!JOBNAME", Label = "Job name" }
        };

        if (kind == StoreCampaignKind.AbandonedCarts)
        {
            tokens.Insert(1, new StoreCampaignTokenDto
            {
                Token = CartSkusToken,
                Label = "The items left in this cart"
            });
        }

        return tokens;
    }

    // ═══════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════

    private async Task<int> ResolveStoreIdAsync(Guid jobId, CancellationToken ct)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId, ct)
            ?? throw new InvalidOperationException("Store not found for this job.");
        return store.StoreId;
    }

    private static int ClampMin(int hours) => Math.Clamp(hours, MinAgeOptions[0], MinAgeOptions[^1]);
    private static int ClampMax(int hours) => Math.Clamp(hours, MaxAgeOptions[0], MaxAgeOptions[^1]);

    /// <summary>Context-free snapshot of one campaign recipient — no EF entity crosses into the engine.</summary>
    private sealed record StoreCampaignRecipient
    {
        public required string FamilyUserId { get; init; }
        public required string FamilyUserName { get; init; }
        public required Guid? RepresentativeRegistrationId { get; init; }
        public required bool OptedOut { get; init; }
        public required List<string> ToAddresses { get; init; }
        public required string CartSkusHtml { get; init; }
    }
}
