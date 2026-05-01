-- Migration: Add bPlayersFullPaymentRequired to Jobs.Jobs
-- Per-job flag: when true, player registrations must pay in full at checkout
-- (no deposit/balance-due flow). Default 0 preserves legacy behavior.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Jobs.Jobs')
    AND name = 'bPlayersFullPaymentRequired'
)
BEGIN
    ALTER TABLE Jobs.Jobs
        ADD bPlayersFullPaymentRequired BIT NOT NULL
        CONSTRAINT DF_Jobs_bPlayersFullPaymentRequired DEFAULT (0);
END
