# Configuration Examples

This document provides minimal, copy-paste friendly examples for configuring Authorize.Net credentials and email settings (Amazon SES) across environments.

## Authorize.Net

Authorize.Net sandbox credentials are required in development. Production credentials are loaded per Customer/Job from the database and must not be placed directly in configuration files.

appsettings.Development.json:
```jsonc
{
  "AuthorizeNet": {
    // Sandbox credential pair used ONLY when ASPNETCORE_ENVIRONMENT=Development.
    // These override legacy hard-coded fallbacks in AdnApiService.
    "SandboxLoginId": "YOUR_SANDBOX_LOGIN_ID",
    "SandboxTransactionKey": "YOUR_SANDBOX_TRANSACTION_KEY"
  }
}
```

Environment variables (alternative to appsettings) – helpful for local secrets:
```
ADN_SANDBOX_LOGINID=YOUR_SANDBOX_LOGIN_ID
ADN_SANDBOX_TRANSACTIONKEY=YOUR_SANDBOX_TRANSACTION_KEY
```

Production (appsettings.Production.json) should omit these sandbox keys. The service `AdnApiService` will fail-fast if production database rows (Customers table) lack `AdnLoginId` or `AdnTransactionKey`.

## Email (Amazon SES)

The `EmailSettings` section controls whether email sending is enabled and how health checks interpret SES quota.

Properties:
- `SupportEmail` (string): Address used for support / reply-to (optional).
- `EmailingEnabled` (bool): Global kill switch. When false, email attempts short-circuit and `EmailHealthService` reports a warning.
- `AwsRegion` (string): Explicit region (e.g. `us-east-1`). If omitted, AWS SDK default chain resolves region.
- `SandboxMode` (bool): Treat SES as sandbox; suppress low quota warnings and allow test scenarios.

appsettings.Development.json example:
```jsonc
{
  "EmailSettings": {
    "SupportEmail": "support-local@example.com",
    "EmailingEnabled": true,
    "AwsRegion": "us-east-1",
    "SandboxMode": true
  }
}
```

appsettings.Production.json example:
```jsonc
{
  "EmailSettings": {
    "SupportEmail": "support@yourdomain.com",
    "EmailingEnabled": true,
    "AwsRegion": "us-east-1",
    "SandboxMode": false
  }
}
```

Disabling all outbound email (e.g. staging environment):
```jsonc
{
  "EmailSettings": {
    "SupportEmail": "support-staging@yourdomain.com",
    "EmailingEnabled": false,
    "SandboxMode": true
  }
}
```

## Health Endpoint Expectations

`GET /api/health/email` returns:
```jsonc
{
  "emailingEnabled": true,
  "isDevelopment": true,
  "sandboxMode": true,
  "sesReachable": true,
  "max24HourSend": 200,
  "sentLast24Hours": 4,
  "maxSendRate": 14.0,
  "region": "us-east-1",
  "warning": null
}
```
Warnings:
- Low quota (non-sandbox & Max24HourSend < 1000): `"Low SES 24h send quota; account may still be in sandbox or recently out of trial."`
- Email disabled: `"Emailing disabled via configuration."`
- SES unreachable: `"Failed to reach SES API."`

## Deployment Notes

1. Keep production Authorize.Net credentials out of source and configuration – they reside in the database (Customers table). Ensure migration or seeding scripts populate them before enabling payments.
2. For local development, prefer environment variables over committing sandbox keys.
3. Rotate Authorize.Net sandbox keys periodically; update environment variables and/or appsettings.Development.json accordingly.
4. If SES quota remains below 1000 after moving out of sandbox, re-verify AWS account identity approval; health endpoint will surface warning until quota increases.

## Quick Checklist
- Development: Sandbox Authorize.Net keys present? EmailSettings.SandboxMode=true? SupportEmail set?
- Production: Customers table rows contain Authorize.Net credentials? EmailSettings.SandboxMode=false? Region correct?
- Staging: EmailingEnabled=false when external email must be suppressed.

## Further Hardening Ideas (Future)
- Add explicit allowlist for test recipient domains when SandboxMode=true.
- Emit remaining quota percentage and estimated hours until depletion.
- Surface DKIM/SPF verification status via extended health model.
