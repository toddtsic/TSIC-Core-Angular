-- Migration: Add LastInvoiceResend to Leagues.teams
-- Throttle field for the AUTOPAY FAILED triage queue's "Send Invoice
-- Reminders" admin action. Nullable; default null preserves legacy
-- behavior (no email has been resent yet).

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Leagues.teams')
    AND name = 'LastInvoiceResend'
)
BEGIN
    ALTER TABLE Leagues.teams
        ADD LastInvoiceResend DATETIME NULL;
END
