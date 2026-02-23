# 019 — ARB Defensive Emails

> **Status**: Plan
> **Date**: 2026-02-23
> **Legacy references**:
> - `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/AdnArb/AdnArbBehindInPaymentController.cs`
> - `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/AdnArb/AdnArbExpiringCardsController.cs`
> - `reference/TSIC-Unify-2024/TSIC-Unify-Services/IAdnTSICService.cs` (shared business logic)
> **Legacy routes**: `AdnArbBehindInPayment/ARBRegistrantsBehind`, `AdnArbExpiringCards/GetList`

---

## 1. Problem Statement

Two legacy MVC controllers handle "something is wrong with an ARB subscription" at different points on the failure timeline:

| Legacy Controller | When | What |
|---|---|---|
| `AdnArbExpiringCards` | **Proactive** — card expires this month | Queries Authorize.Net `cardExpiringThisMonth`, builds registrant list, sends CC-expiration emails + director summary |
| `AdnArbBehindInPayment` | **Reactive** — installment already failed | Queries registrations with ARB subscriptions owing money (>48 hr grace), refreshes live status from Authorize.Net, admin sends customizable template email |

Both share ~80% of their logic:
- Same data model (registrations with ARB subscriptions)
- Same enrichment (join User + FamilyUser + Job)
- Same output (table of flagged registrants with contact info)
- Same action (send batch emails to family contacts)
- Same downstream remediation (update CC + pay balance)

The only difference is the detection mechanism — one asks Authorize.Net "whose cards are expiring?", the other calculates "who owes money based on the billing schedule?"

**Goal**: A single `IArbDefensiveService` with a flag-type discriminator replaces both.

---

## 2. DRY Constraints — Existing Infrastructure to Reuse

### Email (MUST use — no MimeMessage construction)

| Asset | Location | What it provides |
|---|---|---|
| `IEmailService` | `TSIC.Contracts/Services/IEmailService.cs` | `SendAsync(EmailMessageDto)` + `SendBatchAsync(IEnumerable<EmailMessageDto>)` |
| `EmailMessageDto` | same file | `FromName`, `FromAddress`, `ToAddresses`, `Subject`, `HtmlBody` |
| `EmailBatchSendResult` | same file | `AllAddresses` + `FailedAddresses` lists |

**Pattern to follow**: `ReschedulerService.EmailParticipantsAsync()` — one `EmailMessageDto` per recipient, call `SendBatchAsync`, done.

**Email logging**: `IEmailLogRepository` currently has only `GetByJobIdAsync` / `GetDetailAsync` (read methods). We add one write method: `LogAsync(EmailLogs entity)`.

### Authorize.Net (MUST use — no credential handling)

| Asset | Location | What it provides |
|---|---|---|
| `IAdnApiService` | `TSIC.API/Services/Shared/Adn/IAdnApiService.cs` | All ARB methods already ported |
| `GetADNEnvironment()` | same | Resolves sandbox vs production |
| `GetJobAdnCredentials_FromJobId()` | same | Resolves credentials from DB (prod) or config (sandbox) |
| `GetSubscriptionStatus()` | same | Live subscription status lookup |
| `ARBGetSubscriptionListRequest()` | same | Query by `cardExpiringThisMonth` |
| `ADN_UpdateSubscription()` | same | Update CC on subscription (takes `AdnArbUpdateRequest`) |
| `ADN_Charge()` | same | Charge card (takes `AdnChargeRequest`) |

**Usage pattern**: Call `GetADNEnvironment(bProdOnly: true)` + `GetJobAdnCredentials_FromJobId(jobId, true)` once, then pass `env + loginId + transactionKey` to each ARB method.

### Token Substitution (selective reuse)

The existing `ITextSubstitutionService` handles standard tokens (`!JOBNAME`, `!PERSON`, `!AMTFEES`, etc.) but NOT ARB-specific tokens (`!SUBSCRIPTIONID`, `!SUBSCRIPTIONSTATUS`, `!OWEDNOW`).

**Approach**: ARB defensive service does its own simple `string.Replace` pass for the ~10 ARB-specific tokens. The data is already loaded in the DTO — no DB lookups needed. Keeps it decoupled from the general-purpose registration-email substitution system.

---

## 3. Scope

### In Scope
- Unified "ARB Health" endpoint that returns flagged subscriptions by problem type
- Batch email with customizable template + variable substitution
- Email logging (add write method to `IEmailLogRepository`)
- Director notification (summary email for expiring-card batches)
- Self-service CC update + balance payment (UpdateCreditCard flow)
- Admin-only for list/send; TeamMember+ for self-service CC update

### Out of Scope
- ARB Sweep (import settled transactions) — future migration
- ARB Failed Transaction Details — future migration
- Creating/canceling subscriptions (handled by payment wizard)

---

## 4. DTOs (`TSIC.Contracts/Dtos/Arb/`)

### Enum

```csharp
public enum ArbFlagType
{
    ExpiringCard,
    BehindInPayment
}
```

### Flagged Registrant (unified output — both flag types produce this)

```csharp
public record ArbFlaggedRegistrantDto
{
    public required Guid RegistrationId { get; init; }
    public required string SubscriptionId { get; init; }
    public required string SubscriptionStatus { get; init; }
    public required ArbFlagType FlagType { get; init; }

    // Registrant
    public required string RegistrantName { get; init; }
    public string? Assignment { get; init; }
    public string? FamilyUsername { get; init; }
    public string? Role { get; init; }
    public string? RegistrantEmail { get; init; }

    // Contact — Mom
    public string? MomName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomPhone { get; init; }

    // Contact — Dad
    public string? DadName { get; init; }
    public string? DadEmail { get; init; }
    public string? DadPhone { get; init; }

    // Financials
    public decimal FeeTotal { get; init; }
    public decimal PaidTotal { get; init; }
    public decimal CurrentlyOwes { get; init; }
    public decimal OwedTotal { get; init; }

    // Schedule
    public DateTime? NextPaymentDate { get; init; }
    public string? PaymentProgress { get; init; }  // "3 of 6"

    // Job context
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
}
```

### Email Request / Result

```csharp
public record ArbSendEmailsRequest
{
    public required Guid JobId { get; init; }
    public required string SenderUserId { get; init; }
    public required ArbFlagType FlagType { get; init; }
    public required string EmailSubject { get; init; }
    public required string EmailBody { get; init; }  // with !VARIABLE placeholders
    public required List<Guid> RegistrationIds { get; init; }  // selected subset
    public bool NotifyDirectors { get; init; }
}

public record ArbEmailResultDto
{
    public required int EmailsSent { get; init; }
    public required int EmailsFailed { get; init; }
    public List<string> FailedAddresses { get; init; } = [];
}
```

### Self-Service CC Update

```csharp
public record ArbSubscriptionInfoDto
{
    public required string SubscriptionId { get; init; }
    public required string SubscriptionStatus { get; init; }
    public required decimal ChargePerOccurrence { get; init; }
    public required decimal BalanceDue { get; init; }
    public required string RegistrantName { get; init; }
    public required string JobName { get; init; }
    public required DateTime StartDate { get; init; }
    public required int TotalOccurrences { get; init; }
    public required int IntervalMonths { get; init; }
}

public record ArbUpdateCcRequest
{
    public required Guid RegistrationId { get; init; }
    public required string SubscriptionId { get; init; }
    public required string CardNumber { get; init; }
    public required string CardCode { get; init; }
    public required string ExpirationMonth { get; init; }
    public required string ExpirationYear { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required string Email { get; init; }
    public required decimal BalanceDue { get; init; }
}

public record ArbUpdateCcResultDto
{
    public required bool SubscriptionUpdated { get; init; }
    public required bool BalanceCharged { get; init; }
    public decimal AmountCharged { get; init; }
    public string? TransactionId { get; init; }
    public required string Message { get; init; }
}
```

---

## 5. Repository Layer

### Extend: `IEmailLogRepository` — add write method

```csharp
Task LogAsync(EmailLogs entry, CancellationToken ct = default);
```

Implementation: `_context.EmailLogs.Add(entry); await _context.SaveChangesAsync(ct);`

### New: `IArbSubscriptionRepository`

Pure DB queries — no Authorize.Net calls, no business logic.

```csharp
public interface IArbSubscriptionRepository
{
    Task<List<ArbRegistrationProjection>> GetActiveSubscriptionsForJobAsync(
        Guid jobId, CancellationToken ct = default);

    Task<List<ArbRegistrationProjection>> GetRegistrationsByInvoiceNumbersAsync(
        List<string> invoiceNumbers, Guid? jobIdFilter,
        CancellationToken ct = default);

    Task<ArbRegistrationDetail?> GetRegistrationArbDetailAsync(
        Guid registrationId, CancellationToken ct = default);

    Task<decimal> GetArbPaymentsTotalAsync(
        Guid registrationId, CancellationToken ct = default);

    Task<List<ArbDirectorProjection>> GetDirectorsForJobsAsync(
        List<Guid> jobIds, CancellationToken ct = default);

    Task<(string Email, string DisplayName)?> GetSenderInfoAsync(
        string userId, CancellationToken ct = default);

    Task UpdateSubscriptionStatusAsync(
        Guid registrationId, string newStatus, CancellationToken ct = default);

    Task RecordPaymentAsync(
        RegistrationAccounting entry, decimal amount, string userId,
        CancellationToken ct = default);
}
```

**Projection records** (in `TSIC.Contracts/Dtos/Arb/`):

```csharp
public record ArbRegistrationProjection
{
    public required Guid RegistrationId { get; init; }
    public required string SubscriptionId { get; init; }
    public string? SubscriptionStatus { get; init; }
    public DateTime? SubscriptionStartDate { get; init; }
    public int? BillingOccurrences { get; init; }
    public decimal? AmountPerOccurrence { get; init; }
    public int? IntervalLength { get; init; }
    public required string RegistrantName { get; init; }
    public string? Assignment { get; init; }
    public string? FamilyUsername { get; init; }
    public string? Role { get; init; }
    public string? RegistrantEmail { get; init; }
    public string? MomName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomPhone { get; init; }
    public string? DadName { get; init; }
    public string? DadEmail { get; init; }
    public string? DadPhone { get; init; }
    public decimal FeeTotal { get; init; }
    public decimal PaidTotal { get; init; }
    public decimal OwedTotal { get; init; }
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
    public Guid JobId { get; init; }
}

public record ArbRegistrationDetail
{
    public required Guid RegistrationId { get; init; }
    public required Guid JobId { get; init; }
    public required string SubscriptionId { get; init; }
    public string? SubscriptionStatus { get; init; }
    public DateTime? SubscriptionStartDate { get; init; }
    public int? BillingOccurrences { get; init; }
    public decimal? AmountPerOccurrence { get; init; }
    public int? IntervalLength { get; init; }
    public required string RegistrantName { get; init; }
    public required string JobName { get; init; }
    public decimal FeeTotal { get; init; }
    public decimal PaidTotal { get; init; }
    public string? FirstInvoiceNumber { get; init; }
}

public record ArbDirectorProjection
{
    public required Guid JobId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
}
```

---

## 6. Service Layer

### Interface: `IArbDefensiveService` (`TSIC.Contracts/Services/`)

```csharp
public interface IArbDefensiveService
{
    Task<List<ArbFlaggedRegistrantDto>> GetFlaggedSubscriptionsAsync(
        Guid jobId, ArbFlagType flagType, CancellationToken ct = default);

    Task<ArbEmailResultDto> SendDefensiveEmailsAsync(
        ArbSendEmailsRequest request, CancellationToken ct = default);

    Task<ArbSubscriptionInfoDto?> GetSubscriptionInfoAsync(
        Guid registrationId, CancellationToken ct = default);

    Task<ArbUpdateCcResultDto> UpdateSubscriptionCreditCardAsync(
        ArbUpdateCcRequest request, string userId, CancellationToken ct = default);
}
```

### Implementation: `ArbDefensiveService` (`TSIC.API/Services/Admin/`)

**Dependencies** (all existing — nothing new created):

```csharp
public class ArbDefensiveService : IArbDefensiveService
{
    private readonly IArbSubscriptionRepository _arbRepo;
    private readonly IEmailLogRepository _emailLogRepo;
    private readonly IAdnApiService _adnApi;
    private readonly IEmailService _emailService;
    private readonly ILogger<ArbDefensiveService> _logger;
}
```

### GetFlaggedSubscriptionsAsync — flow

```
if ExpiringCard:
    1. env = _adnApi.GetADNEnvironment(bProdOnly: true)
    2. creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId, true)
    3. response = _adnApi.ARBGetSubscriptionListRequest(env, creds.LoginId, creds.Key, cardExpiringThisMonth)
    4. invoices = response.subscriptionDetails.Select(x => x.invoice)
    5. regs = await _arbRepo.GetRegistrationsByInvoiceNumbersAsync(invoices, jobId)
    6. Map to ArbFlaggedRegistrantDto (CurrentlyOwes = 0 for this type)

if BehindInPayment:
    1. regs = await _arbRepo.GetActiveSubscriptionsForJobAsync(jobId)
    2. For each: calculate FeesOwedAsOfToday using ARB schedule math
    3. Filter: FeesOwedAsOfToday > 0 AND outside 48hr grace window
    4. env + creds = resolve once (same as above)
    5. For remaining: _adnApi.GetSubscriptionStatus() to refresh
    6. await _arbRepo.UpdateSubscriptionStatusAsync() for stale statuses
    7. Filter out canceled
    8. Calculate NextPaymentDate + PaymentProgress
    9. Map to ArbFlaggedRegistrantDto
```

### ARB Schedule Math (ported from legacy `AdnTSICService`)

Three pure private methods:

```csharp
private static int GetOccurrencesAsOfNow(int total, DateTime start, int intervalMonths)
private static decimal CalculateFeesOwed(ArbRegistrationProjection reg, int occurrences)
private static DateTime? CalculateNextPaymentDate(DateTime start, int intervalMonths, int total)
```

### SendDefensiveEmailsAsync — flow

```
1. senderInfo = await _arbRepo.GetSenderInfoAsync(request.SenderUserId)
2. flaggedRegs = filter full list to request.RegistrationIds
3. messages = flaggedRegs.Select(reg => new EmailMessageDto {
       FromName = senderInfo.DisplayName,
       FromAddress = senderInfo.Email,
       ToAddresses = CollectValidEmails(reg.MomEmail, reg.DadEmail, reg.RegistrantEmail),
       Subject = request.EmailSubject,
       HtmlBody = ReplaceArbTokens(request.EmailBody, reg)
   })
4. result = await _emailService.SendBatchAsync(messages)
5. await _emailLogRepo.LogAsync(new EmailLogs { ... })
6. Send confirmation to sender via _emailService.SendAsync()
7. if NotifyDirectors:
       directors = await _arbRepo.GetDirectorsForJobsAsync(...)
       Send summary to each director via _emailService.SendAsync()
8. Return ArbEmailResultDto
```

### Token Substitution (simple — data already in DTO)

```csharp
private static string ReplaceArbTokens(string template, ArbFlaggedRegistrantDto reg)
{
    return template
        .Replace("!PLAYER", $"<strong>{reg.RegistrantName}</strong>")
        .Replace("!SUBSCRIPTIONID", $"<strong>{reg.SubscriptionId}</strong>")
        .Replace("!SUBSCRIPTIONSTATUS", $"<strong>{reg.SubscriptionStatus}</strong>")
        .Replace("!FEETOTAL", $"<strong>{reg.FeeTotal:C}</strong>")
        .Replace("!PAIDTOTAL", $"<strong>{reg.PaidTotal:C}</strong>")
        .Replace("!OWEDNOW", $"<strong>{reg.CurrentlyOwes:C}</strong>")
        .Replace("!OWEDTOTAL", $"<strong>{reg.OwedTotal:C}</strong>")
        .Replace("!FAMILYUSERNAME", $"<strong>{reg.FamilyUsername}</strong>")
        .Replace("!JOBLINK", BuildJobLink(reg.JobPath))
        .Replace("!JOBNAME", $"<strong>{reg.JobName}</strong>");
}
```

### UpdateSubscriptionCreditCardAsync — flow

```
1. detail = await _arbRepo.GetRegistrationArbDetailAsync(registrationId)
2. Validate detail.SubscriptionId == request.SubscriptionId
3. env + creds = resolve via _adnApi
4. Validate card: _adnApi.ADN_Authorize(new AdnAuthorizeRequest { ... })
5. Update subscription: _adnApi.ADN_UpdateSubscription(new AdnArbUpdateRequest { ... })
6. if BalanceDue > 0:
       Charge card: _adnApi.ADN_Charge(new AdnChargeRequest { ... })
       await _arbRepo.RecordPaymentAsync(accounting entry, amount, userId)
7. Return ArbUpdateCcResultDto
```

---

## 7. Controller (`TSIC.API/Controllers/ArbDefensiveController.cs`)

| Method | Route | Auth | Returns |
|---|---|---|---|
| GET | `/api/arb-defensive/flagged?type={ExpiringCard\|BehindInPayment}` | Admin | `List<ArbFlaggedRegistrantDto>` |
| GET | `/api/arb-defensive/substitution-variables` | Admin | `List<SubstitutionVariableDto>` |
| POST | `/api/arb-defensive/send-emails` | Admin | `ArbEmailResultDto` |
| GET | `/api/arb-defensive/subscription-info/{registrationId}` | TeamMember+ | `ArbSubscriptionInfoDto` |
| POST | `/api/arb-defensive/update-cc` | TeamMember+ | `ArbUpdateCcResultDto` |

All scoped to current job via JWT `jobPath`.

---

## 8. Frontend

### Route: `/:jobPath/admin/arb-health` — admin-only, lazy-loaded

### ArbHealthComponent — two-tab layout

**Tabs**: Expiring Cards | Behind in Payment

Each tab loads via `GET /api/arb-defensive/flagged?type=...`

**Table columns** (shared):
Checkbox | Registrant | Subscription ID | Status | Mom (name+email) | Dad (name+email) | Currently Owes | Next Payment | Progress

**Batch email panel** (shown when rows checked):
- Subject input
- Body textarea with substitution variable dropdown
- "Notify Directors" toggle (default ON for Expiring Cards)
- "Send to Selected" button

### ArbUpdateCcComponent

**Route**: `/:jobPath/arb/update-cc/:registrationId` (TeamMember+)

Card form with balance-due display + submit.

### arb-defensive.service.ts

Four methods matching the 4 API endpoints, returning `Observable<T>`.

---

## 9. Files

### Created (18)

| File | Layer |
|---|---|
| `TSIC.Contracts/Dtos/Arb/ArbFlagType.cs` | Enum |
| `TSIC.Contracts/Dtos/Arb/ArbFlaggedRegistrantDto.cs` | DTO |
| `TSIC.Contracts/Dtos/Arb/ArbSendEmailsRequest.cs` | DTO |
| `TSIC.Contracts/Dtos/Arb/ArbEmailResultDto.cs` | DTO |
| `TSIC.Contracts/Dtos/Arb/ArbSubscriptionInfoDto.cs` | DTO |
| `TSIC.Contracts/Dtos/Arb/ArbUpdateCcRequest.cs` | DTO |
| `TSIC.Contracts/Dtos/Arb/ArbUpdateCcResultDto.cs` | DTO |
| `TSIC.Contracts/Dtos/Arb/ArbRegistrationProjection.cs` | Repo projections |
| `TSIC.Contracts/Repositories/IArbSubscriptionRepository.cs` | Interface |
| `TSIC.Infrastructure/Repositories/ArbSubscriptionRepository.cs` | Implementation |
| `TSIC.Contracts/Services/IArbDefensiveService.cs` | Interface |
| `TSIC.API/Services/Admin/ArbDefensiveService.cs` | Implementation |
| `TSIC.API/Controllers/ArbDefensiveController.cs` | Controller |
| `views/admin/arb-health/arb-health.component.ts` | Main component |
| `views/admin/arb-health/arb-health.component.html` | Template |
| `views/admin/arb-health/arb-health.component.scss` | Styles |
| `views/admin/arb-health/services/arb-defensive.service.ts` | Frontend service |
| `views/admin/arb-health/arb-update-cc/arb-update-cc.component.ts` | CC update (+html/scss) |

### Modified (4)

| File | Change |
|---|---|
| `TSIC.Contracts/Repositories/IEmailLogRepository.cs` | Add `LogAsync` write method |
| `TSIC.Infrastructure/Repositories/EmailLogRepository.cs` | Implement `LogAsync` |
| `TSIC.API/Program.cs` | DI: `IArbSubscriptionRepository`, `IArbDefensiveService` |
| `app.routes.ts` | Routes: `admin/arb-health`, `arb/update-cc/:registrationId` |

---

## 10. Implementation Order

| Phase | What | Depends On |
|---|---|---|
| **1** | DTOs + Enum | Nothing |
| **2** | `IEmailLogRepository.LogAsync` | Nothing |
| **3** | `IArbSubscriptionRepository` + impl | DTOs, entities |
| **4** | `IArbDefensiveService` + impl | Repo, IAdnApiService, IEmailService |
| **5** | Controller | Service |
| **6** | Regen API models | Controller compiles |
| **7** | Frontend service + ArbHealthComponent | API models |
| **8** | Batch email panel | Phase 7 |
| **9** | ArbUpdateCcComponent | API models |

---

## 11. Design Decisions

**Single interface**: Both controllers detect the same thing via different signals. One `if` branch handles the difference.

**No new email infra**: Legacy built MimeMessage directly. Modern `IEmailService.SendBatchAsync` handles MimeKit+SES internally. Zero MimeMessage construction in ARB service.

**No new ADN infra**: All ARB methods already on `IAdnApiService`. Existing `AdnArbUpdateRequest`/`AdnChargeRequest` records in Contracts.

**Local token substitution**: 10 tokens mapping to DTO fields. No need to couple to the general-purpose `ITextSubstitutionService`.

**48hr grace window**: Preserved from legacy. Prevents false-positive "behind" flags during ARB sweep delay.

**TeamMember+ for UpdateCreditCard**: Self-service. Admin sends email; cardholder follows link and fixes.
