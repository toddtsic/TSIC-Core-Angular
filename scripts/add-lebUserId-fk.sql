-- ============================================================
-- Add missing FK on LebUserId → dbo.AspNetUsers(Id)
-- Targets: new schemas only (scheduling, stores)
-- Idempotent: checks for existing constraint before adding
-- ============================================================

-- 1. scheduling.DivisionProcessingOrder
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID('scheduling.DivisionProcessingOrder')
      AND name = 'FK_DivisionProcessingOrder_LebUser'
)
BEGIN
    ALTER TABLE scheduling.DivisionProcessingOrder
        ADD CONSTRAINT FK_DivisionProcessingOrder_LebUser
        FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id);
    PRINT 'Added FK: scheduling.DivisionProcessingOrder.LebUserId';
END
ELSE
    PRINT 'Skipped: scheduling.DivisionProcessingOrder (FK exists)';
GO

-- 2. stores.StoreItemImage — widen column first (was NVARCHAR(128), needs 450)
IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'stores' AND t.name = 'StoreItemImage'
      AND c.name = 'LebUserId' AND c.max_length < 900  -- 450 chars × 2 bytes = 900
)
BEGIN
    ALTER TABLE stores.StoreItemImage
        ALTER COLUMN LebUserId NVARCHAR(450) NOT NULL;
    PRINT 'Widened: stores.StoreItemImage.LebUserId → NVARCHAR(450)';
END
ELSE
    PRINT 'Skipped: stores.StoreItemImage.LebUserId already NVARCHAR(450)';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID('stores.StoreItemImage')
      AND name = 'FK_StoreItemImage_LebUser'
)
BEGIN
    ALTER TABLE stores.StoreItemImage
        ADD CONSTRAINT FK_StoreItemImage_LebUser
        FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id);
    PRINT 'Added FK: stores.StoreItemImage.LebUserId';
END
ELSE
    PRINT 'Skipped: stores.StoreItemImage (FK exists)';
GO

-- Verification
SELECT
    OBJECT_SCHEMA_NAME(fk.parent_object_id) AS [Schema],
    OBJECT_NAME(fk.parent_object_id) AS [Table],
    fk.name AS [FK Name]
FROM sys.foreign_keys fk
WHERE fk.name LIKE 'FK_%_LebUser'
  AND OBJECT_SCHEMA_NAME(fk.parent_object_id) IN ('scheduling', 'stores', 'fees')
ORDER BY 1, 2;
GO
