## VerticalInsure / RegSaver Independent Purchase Architecture

### Overview
Player registration fees (Authorize.Net PIF / Deposit / ARB) are fully decoupled from insurance purchases. The insurance flow never triggers, depends on, or alters payment gateway logic. It only persists returned RegSaver policy numbers onto existing `Registrations` rows.

### Sequence
1. Client requests pre-submit snapshot (`PreSubmitInsuranceDto`) after pending registrations created.
2. Snapshot contains VI player object with quote-able products; user selects quotes.
3. Client calls `POST /api/insurance/purchase` with: `JobId`, `FamilyUserId`, `RegistrationIds[]`, `QuoteIds[]`, optional `token` or card data (future enhancement). Current controller passes `null` for token/card until UI wiring.
4. Service validates registration ownership & count alignment, prevents duplicate policy creation, then:
   - Builds batch purchase payload (quotes + payment_method).
   - Executes VerticalInsure HTTP request (Basic auth, endpoint: `v1/purchase/registration-cancellation/batch`).
   - Filters ACTIVE policies and persists `RegsaverPolicyId` & `RegsaverPolicyIdCreateDate`.
5. Response returns `{ Success, Policies{registrationId: policyNumber}, Error? }`.

### Decoupling Points
- `PaymentService` only persists insurance policy if client supplies policy number when paying; it never calls purchase.
- `VerticalInsureService` never touches Authorize.Net models, subscriptions, or accounting entries.
- Shared DTO fields (`ViPolicyNumber`, etc.) serve as passive metadata; no cross-service invocation.

### Validation Rules
- RegistrationIds and QuoteIds must be non-empty and same count.
- Each registration must belong to the calling family and match supplied JobId.
- Registration must not already have a `RegsaverPolicyId`.
- HTTP non-200/201 → failure, no persistence.

### Payment Method Handling
`payment_method.token` (prefix `stripe:`) OR `payment_method.card` with fields: number, verification, month (MM), year (YYYY), name, postal code. Only one is populated.

### Environment & Configuration
- Client Id: Dev vs Prod (hardcoded identifiers, may move to config).
- Secret: `VI_DEV_SECRET` / `VI_PROD_SECRET` environment variables.
- Base URL: Named HttpClient "verticalinsure" uses configuration key `VerticalInsure:BaseUrl` or fallback environment `VI_BASE_URL`; dev default can point to sandbox.

### Error Handling & Retries
- Transient HTTP failures return `Success=false` with an error message; caller may retry idempotently since policies aren’t persisted on failure.
- Duplicate purchase attempts fail fast before external call when any registration already has a policy.

### Data Persistence
Fields written per successful policy: `RegsaverPolicyId`, `RegsaverPolicyIdCreateDate`, `Modified`, `LebUserId` (family user).

### OpenAPI Exposure
Add endpoint to Swagger: ensure controller annotated with response types (already present). After build, fetch spec via `scripts/fetch-swagger.ps1`.

### Angular Client Regeneration
1. Start API locally (dotnet run / watcher) ensuring Swagger enabled.
2. Run PowerShell script to fetch spec:
   ```powershell
   scripts\fetch-swagger.ps1 > swagger.json
   ```
3. Use OpenAPI generator (example if installed globally):
   ```powershell
   npx @openapitools/openapi-generator-cli generate -i swagger.json -g typescript-angular -o src\frontend\tsic-app\src\app\api
   ```
4. Commit regenerated client; verify new `insurancePurchase` method surfaces.

### Future Enhancements
- Wire token/card inputs from UI to controller; extend controller signature and pass through to service.
- Move client id values to configuration to avoid hardcoding.
- Add retry/backoff for transient network failures.
- Add policy reconciliation job for auditing ACTIVE status post-purchase.

### Test Coverage Additions (Planned)
- Token payload formatting (`stripe:{token}`).
- Card expiry expansion (MMYY → month/year mapping).
- HTTP failure path (500) returns `Success=false` with no persisted policy.

### Security Considerations
- Secrets only in environment variables; never logged.
- Basic auth string constructed per request; avoid long-lived headers.
- Validate ownership to prevent policy association with another family’s registrations.

### Edge Cases
- Empty arrays → immediate failure (no external call).
- Mismatch counts → failure.
- Already insured registration among batch → failure (no partial success).
- Some policies inactive or missing number → skipped (others may persist). Success still true if at least one policy stored.

### Invariants
- On success, `Policies.Count` equals number of ACTIVE policy numbers stored (may be less than requested if external response omits some). Registration rows updated atomically in single SaveChanges call.
- On failure, zero rows altered.
