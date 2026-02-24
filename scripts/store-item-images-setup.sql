-- ============================================================================
-- Store Item Images — Table + Seed Data
-- Created: 2026-02-24
--
-- PURPOSE: Proper image tracking for store items. Each row = one image with
-- full URL, display order, and alt text. Replaces the ImageCount approach.
--
-- INSTRUCTIONS:
--   1. Run against dev first, verify, then run against prod
--   2. After running, re-scaffold EF Core entities (dev only)
--   3. The seed data section inserts legacy images from
--      https://statics.teamsportsinfo.com/Store-Sku-Images/
-- ============================================================================

-- ── Step 1: Create table ──

IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'stores' AND t.name = 'StoreItemImage')
BEGIN
    CREATE TABLE stores.StoreItemImage (
        StoreItemImageId    INT IDENTITY(1,1) PRIMARY KEY,
        StoreItemId         INT NOT NULL,
        ImageUrl            NVARCHAR(500) NOT NULL,
        DisplayOrder        INT NOT NULL DEFAULT 0,
        AltText             NVARCHAR(200) NULL,
        Modified            DATETIME NOT NULL DEFAULT GETUTCDATE(),
        LebUserId           NVARCHAR(128) NOT NULL,

        CONSTRAINT FK_StoreItemImage_StoreItem
            FOREIGN KEY (StoreItemId) REFERENCES stores.StoreItems(StoreItemId)
    );

    PRINT 'Created stores.StoreItemImage table.';
END
ELSE
BEGIN
    PRINT 'stores.StoreItemImage already exists — skipping CREATE.';
END
GO

-- ── Step 2: Seed legacy images ──
-- Source: https://statics.teamsportsinfo.com/Store-Sku-Images/
-- Convention: {storeId}-{storeItemId}-{instance}.jpg
-- Uses StoreId + StoreItemId compound match to avoid orphan mismatches.

SET NOCOUNT ON;

DECLARE @cdn NVARCHAR(200) = 'https://statics.teamsportsinfo.com/Store-Sku-Images/';
DECLARE @sys NVARCHAR(128) = '71765055-647D-432E-AFB6-0F84218D0247'; -- SuperUser
DECLARE @now DATETIME = GETUTCDATE();

-- Clear existing seed data (idempotent re-run)
DELETE FROM stores.StoreItemImage;
PRINT 'Cleared existing StoreItemImage rows.';

-- ── Store 3 ──
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
SELECT si.StoreItemId, @cdn + v.FileName, v.DisplayOrder, @now, @sys
FROM (VALUES
    (3, 8,  '3-8-1.jpg',   0),
    (3, 9,  '3-9-1.jpg',   0),
    (3, 10, '3-10-1.jpg',  0),
    (3, 11, '3-11-1.jpg',  0),
    (3, 12, '3-12-1.jpg',  0)
) AS v(StoreId, StoreItemId, FileName, DisplayOrder)
JOIN stores.StoreItems si ON si.StoreId = v.StoreId AND si.StoreItemId = v.StoreItemId;

-- ── Store 4 ──
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
SELECT si.StoreItemId, @cdn + v.FileName, v.DisplayOrder, @now, @sys
FROM (VALUES
    (4, 10, '4-10-1.jpg',  0),
    (4, 10, '4-10-2.jpg',  1),
    (4, 13, '4-13-1.jpg',  0),
    (4, 14, '4-14-1.jpg',  0),
    (4, 15, '4-15-1.jpg',  0),
    (4, 16, '4-16-1.jpg',  0),
    (4, 17, '4-17-1.jpg',  0)
) AS v(StoreId, StoreItemId, FileName, DisplayOrder)
JOIN stores.StoreItems si ON si.StoreId = v.StoreId AND si.StoreItemId = v.StoreItemId;

-- ── Store 5 ──
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
SELECT si.StoreItemId, @cdn + v.FileName, v.DisplayOrder, @now, @sys
FROM (VALUES
    (5, 12, '5-12-1.jpg',  0),
    (5, 13, '5-13-1.jpg',  0),
    (5, 13, '5-13-2.jpg',  1),
    (5, 13, '5-13-3.jpg',  2),
    (5, 18, '5-18-1.jpg',  0),
    (5, 19, '5-19-1.jpg',  0),
    (5, 20, '5-20-1.jpg',  0),
    (5, 21, '5-21-1.jpg',  0),
    (5, 22, '5-22-1.jpg',  0)
) AS v(StoreId, StoreItemId, FileName, DisplayOrder)
JOIN stores.StoreItems si ON si.StoreId = v.StoreId AND si.StoreItemId = v.StoreItemId;

-- ── Store 6 ──
INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)
SELECT si.StoreItemId, @cdn + v.FileName, v.DisplayOrder, @now, @sys
FROM (VALUES
    (6, 14, '6-14-1.jpg',  0),
    (6, 23, '6-23-1.jpg',  0),
    (6, 24, '6-24-1.jpg',  0),
    (6, 25, '6-25-1.jpg',  0),
    (6, 26, '6-26-1.jpg',  0),
    (6, 27, '6-27-1.jpg',  0)
) AS v(StoreId, StoreItemId, FileName, DisplayOrder)
JOIN stores.StoreItems si ON si.StoreId = v.StoreId AND si.StoreItemId = v.StoreItemId;

-- ── Verification ──
PRINT '';
PRINT '══════════════════════════════════════════════';
PRINT ' StoreItemImage Seed — Results';
PRINT '══════════════════════════════════════════════';

SELECT
    si.StoreId,
    img.StoreItemId,
    si.StoreItemName,
    img.DisplayOrder,
    img.ImageUrl
FROM stores.StoreItemImage img
JOIN stores.StoreItems si ON img.StoreItemId = si.StoreItemId
ORDER BY si.StoreId, img.StoreItemId, img.DisplayOrder;

SELECT
    COUNT(*) AS [Total Image Rows],
    COUNT(DISTINCT StoreItemId) AS [Items With Images]
FROM stores.StoreItemImage;

-- ══════════════════════════════════════════════════════════════════════
-- AMBIGUOUS FILES (2-part names — don't match {storeId}-{itemId}-{instance})
-- These were NOT seeded. Investigate manually:
--   1-1.jpg    — Store 1? Old format?
--   2-1.jpg    — Store 2, instance 1?
--   2-2.jpg    — Store 2, instance 2?
--   19-1.jpg   — StoreItemId 19? No storeId prefix?
--   20-1.jpg   — StoreItemId 20? No storeId prefix?
--   20-2.jpg   — StoreItemId 20? No storeId prefix?
-- ══════════════════════════════════════════════════════════════════════

SET NOCOUNT OFF;

