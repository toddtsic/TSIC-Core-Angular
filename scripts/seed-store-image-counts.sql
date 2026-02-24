-- ============================================================================
-- Seed Store Item ImageCount from legacy Store-Sku-Images folder
-- Generated: 2026-02-24
--
-- Source: statics.teamsportsinfo.com/Store-Sku-Images/
-- Convention: {storeId}-{storeItemId}-{instance}.jpg
-- ImageCount = max instance per (StoreId, StoreItemId) pair
--
-- Uses StoreId + StoreItemId to avoid orphan file mismatches.
-- Only rows where BOTH match will be updated.
-- ============================================================================

SET NOCOUNT ON;

PRINT 'Seeding stores.StoreItems.ImageCount from legacy file inventory...';
PRINT '';

-- Reset all to 0 first (clean slate)
UPDATE stores.StoreItems SET ImageCount = 0;

-- ── Store 3 ──
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 3 AND StoreItemId = 8;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 3 AND StoreItemId = 9;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 3 AND StoreItemId = 10;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 3 AND StoreItemId = 11;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 3 AND StoreItemId = 12;

-- ── Store 4 ──
UPDATE stores.StoreItems SET ImageCount = 2 WHERE StoreId = 4 AND StoreItemId = 10;  -- 4-10-1, 4-10-2
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 4 AND StoreItemId = 13;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 4 AND StoreItemId = 14;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 4 AND StoreItemId = 15;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 4 AND StoreItemId = 16;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 4 AND StoreItemId = 17;

-- ── Store 5 ──
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 5 AND StoreItemId = 12;
UPDATE stores.StoreItems SET ImageCount = 3 WHERE StoreId = 5 AND StoreItemId = 13;  -- 5-13-1, 5-13-2, 5-13-3
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 5 AND StoreItemId = 18;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 5 AND StoreItemId = 19;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 5 AND StoreItemId = 20;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 5 AND StoreItemId = 21;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 5 AND StoreItemId = 22;

-- ── Store 6 ──
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 6 AND StoreItemId = 14;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 6 AND StoreItemId = 23;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 6 AND StoreItemId = 24;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 6 AND StoreItemId = 25;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 6 AND StoreItemId = 26;
UPDATE stores.StoreItems SET ImageCount = 1 WHERE StoreId = 6 AND StoreItemId = 27;

-- ── Verification ──
PRINT '';
PRINT '══════════════════════════════════════════════';
PRINT ' ImageCount Seed — Results';
PRINT '══════════════════════════════════════════════';

SELECT
    s.StoreId,
    si.StoreItemId,
    si.StoreItemName,
    si.ImageCount
FROM stores.StoreItems si
JOIN stores.Stores s ON si.StoreId = s.StoreId
WHERE si.ImageCount > 0
ORDER BY s.StoreId, si.StoreItemId;

PRINT '';
SELECT
    COUNT(*) AS [Items With Images],
    SUM(ImageCount) AS [Total Image Count]
FROM stores.StoreItems
WHERE ImageCount > 0;

-- ══════════════════════════════════════════════════════════════════════
-- AMBIGUOUS FILES (2-part names — don't match {storeId}-{itemId}-{instance} convention)
-- These were NOT seeded. Investigate manually:
--   1-1.jpg    — Store 1? Old format?
--   2-1.jpg    — Store 2, instance 1?
--   2-2.jpg    — Store 2, instance 2?
--   19-1.jpg   — StoreItemId 19? No storeId prefix?
--   20-1.jpg   — StoreItemId 20? No storeId prefix?
--   20-2.jpg   — StoreItemId 20? No storeId prefix?
-- ══════════════════════════════════════════════════════════════════════

SET NOCOUNT OFF;
