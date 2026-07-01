-- ============================================================
-- Seed: Single-Elimination (SE) templates — sizes 2..64
--
-- Populates brackets.Strategies('SE'), brackets.Templates,
-- brackets.TemplateGames, brackets.AdvancementRoutes with the
-- standard single-elim topology, GENERATED set-based (not
-- hand-typed) for accuracy.
--
-- Seed numbering = canonical bracket fold (1 vs N, halves
-- balanced so top seeds meet latest):
--   size 16 first round = (1,16)(8,9)(4,13)(5,12)(2,15)(7,10)(3,14)(6,11)
--
-- Round ladder (teams entering → type): 64=Z 32=Y 16=X 8=Q 4=S 2=F.
-- A size-N template contains every round with RoundSize <= N;
-- its first round is RoundSize = N.
--
-- GameKey = 1-based position of the game within its round (top→bottom).
-- Winner routing: game p feeds parent ceil(p/2), slot = (p odd ? 1 : 2).
--
-- Idempotent / reseed-safe:
--   * Ensures the 'SE' strategy row exists.
--   * REFUSES to reseed if any BracketInstances reference SE
--     templates (protects live brackets) — prints and exits.
--   * Otherwise clears & rebuilds SE template data in a transaction.
--
-- Bronze / 3rd-place game (RoundType 'B'):
--   One loser-fed game — both semifinal LOSERS play; winner = 3rd, loser = 4th.
--   Included in every template of size >= 4 (needs semis) and flagged
--   IsOptional = 1. It is NOT a separate template/variant: the scheduling
--   quick-entry auto-places the 'B' game after 'F', and the OPTIONAL part is
--   whether the scheduler keeps that placed game or removes it. Feeds
--   materialize only for games actually on the schedule.
--
-- Expected results (size >= 4 includes the optional bronze: +1 game, +2 routes):
--     size  2 →  1 game ,  0 routes
--     size  4 →  4 games,  4 routes
--     size  8 →  8 games,  8 routes
--     size 16 → 16 games, 16 routes
--     size 32 → 32 games, 32 routes
--     size 64 → 64 games, 64 routes
-- ============================================================

SET NOCOUNT ON;

-- 1. Ensure the SE strategy row exists
IF NOT EXISTS (SELECT 1 FROM brackets.Strategies WHERE Code = 'SE')
    INSERT brackets.Strategies (Code, Name, Description, IsActive, Modified)
    VALUES ('SE', 'Single Elimination',
            'Single elimination: winners advance, losers are out. No losers bracket.',
            1, GETDATE());

DECLARE @StrategyId INT = (SELECT StrategyId FROM brackets.Strategies WHERE Code = 'SE');

-- 2. Protect live data: refuse to reseed if templates are in use
IF EXISTS (
    SELECT 1
    FROM brackets.BracketInstances bi
    JOIN brackets.Templates t ON t.TemplateId = bi.TemplateId
    WHERE t.StrategyId = @StrategyId)
BEGIN
    PRINT 'SE templates are referenced by existing BracketInstances — reseed SKIPPED to protect live data.';
    RETURN;
END

BEGIN TRY
    BEGIN TRAN;

    -- 3. Clear existing SE template data (FK order: routes → games → templates)
    DELETE r
    FROM brackets.AdvancementRoutes r
    WHERE EXISTS (
        SELECT 1 FROM brackets.TemplateGames g
        JOIN brackets.Templates t ON t.TemplateId = g.TemplateId
        WHERE t.StrategyId = @StrategyId AND g.TemplateGameId = r.SourceTemplateGameId);

    DELETE g
    FROM brackets.TemplateGames g
    JOIN brackets.Templates t ON t.TemplateId = g.TemplateId
    WHERE t.StrategyId = @StrategyId;

    DELETE FROM brackets.Templates WHERE StrategyId = @StrategyId;

    -- 4. Templates — one Standard template per size (bronze rides inside as an optional game)
    INSERT brackets.Templates (StrategyId, BracketSize, Variant, Name, Modified)
    SELECT @StrategyId, sz, 'Standard', CONCAT('SE-', sz), GETDATE()
    FROM (VALUES (2),(4),(8),(16),(32),(64)) v(sz);

    -- 5. TemplateGames — interior games (no seeds) + leaf games (seeded via fold)
    ;WITH rounds(RoundType, RoundSize, RoundOrder) AS (
        SELECT * FROM (VALUES
            ('Z',64,1),('Y',32,2),('X',16,3),('Q',8,4),('S',4,5),('F',2,6)
        ) v(RoundType, RoundSize, RoundOrder)
    ),
    nums(n) AS (
        SELECT * FROM (VALUES
            (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15),(16),
            (17),(18),(19),(20),(21),(22),(23),(24),(25),(26),(27),(28),(29),(30),(31),(32)
        ) v(n)
    ),
    -- canonical bracket seed order: fold(sz, pos, seed)
    fold AS (
        SELECT CAST(1 AS INT) AS sz, CAST(1 AS INT) AS pos, CAST(1 AS INT) AS seed
        UNION ALL
        SELECT f.sz * 2,
               f.pos * 2 - 1 + h.h,
               CASE WHEN h.h = 0 THEN f.seed ELSE f.sz * 2 + 1 - f.seed END
        FROM fold f
        CROSS JOIN (VALUES (0),(1)) h(h)
        WHERE f.sz < 64
    )
    INSERT brackets.TemplateGames
        (TemplateId, RoundType, GameKey, Slot1Seed, Slot2Seed, SortOrder, Modified, LebUserId)
    SELECT t.TemplateId,
           r.RoundType,
           g.n                            AS GameKey,
           f1.seed                        AS Slot1Seed,   -- NULL for interior rounds
           f2.seed                        AS Slot2Seed,
           r.RoundOrder * 100 + g.n       AS SortOrder,
           GETDATE(), NULL
    FROM brackets.Templates t
    JOIN rounds r ON r.RoundSize <= t.BracketSize
    JOIN nums  g ON g.n <= r.RoundSize / 2
    LEFT JOIN fold f1 ON r.RoundSize = t.BracketSize
                     AND f1.sz = t.BracketSize AND f1.pos = 2 * g.n - 1
    LEFT JOIN fold f2 ON r.RoundSize = t.BracketSize
                     AND f2.sz = t.BracketSize AND f2.pos = 2 * g.n
    WHERE t.StrategyId = @StrategyId
    OPTION (MAXRECURSION 0);

    -- 6. AdvancementRoutes — winner of game p → parent ceil(p/2), slot by parity
    ;WITH rounds(RoundType, RoundSize, RoundOrder) AS (
        SELECT * FROM (VALUES
            ('Z',64,1),('Y',32,2),('X',16,3),('Q',8,4),('S',4,5),('F',2,6)
        ) v(RoundType, RoundSize, RoundOrder)
    )
    INSERT brackets.AdvancementRoutes
        (SourceTemplateGameId, SourceResult, TargetTemplateGameId, TargetSlot, Modified, LebUserId)
    SELECT src.TemplateGameId,
           'Winner',
           tgt.TemplateGameId,
           CASE WHEN src.GameKey % 2 = 1 THEN 1 ELSE 2 END AS TargetSlot,
           GETDATE(), NULL
    FROM brackets.TemplateGames src
    JOIN brackets.Templates t ON t.TemplateId = src.TemplateId AND t.StrategyId = @StrategyId
    JOIN rounds rs ON rs.RoundType = src.RoundType
    JOIN rounds rt ON rt.RoundOrder = rs.RoundOrder + 1
    JOIN brackets.TemplateGames tgt ON tgt.TemplateId = src.TemplateId
                                   AND tgt.RoundType = rt.RoundType
                                   AND tgt.GameKey = (src.GameKey + 1) / 2;

    -- 7. Bronze game (RoundType 'B', IsOptional=1) — every size >= 4 (needs semis).
    --    One loser-fed game (no seeds, no out-route): winner = 3rd, loser = 4th.
    INSERT brackets.TemplateGames
        (TemplateId, RoundType, GameKey, Slot1Seed, Slot2Seed, SortOrder, IsOptional, Modified, LebUserId)
    SELECT t.TemplateId, 'B', 1, NULL, NULL, 595, 1, GETDATE(), NULL
    FROM brackets.Templates t
    WHERE t.StrategyId = @StrategyId AND t.BracketSize >= 4;

    -- 8. Bronze loser routes — each semifinal LOSER feeds the bronze game.
    --    Semi GameKey 1 -> bronze slot 1 ; Semi GameKey 2 -> bronze slot 2.
    INSERT brackets.AdvancementRoutes
        (SourceTemplateGameId, SourceResult, TargetTemplateGameId, TargetSlot, Modified, LebUserId)
    SELECT semi.TemplateGameId, 'Loser', bronze.TemplateGameId, semi.GameKey, GETDATE(), NULL
    FROM brackets.Templates t
    JOIN brackets.TemplateGames semi
        ON semi.TemplateId = t.TemplateId AND semi.RoundType = 'S'
    JOIN brackets.TemplateGames bronze
        ON bronze.TemplateId = t.TemplateId AND bronze.RoundType = 'B' AND bronze.GameKey = 1
    WHERE t.StrategyId = @StrategyId AND t.BracketSize >= 4;

    COMMIT;
    PRINT 'SE templates reseeded (with optional bronze game).';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH
GO

-- ============================================================
-- Verification — counts per size vs expected
--   size 2: 1 game, 0 routes   |   size >= 4: N games, N routes (incl. optional bronze)
-- ============================================================
SELECT
    t.BracketSize,
    CASE WHEN t.BracketSize >= 4 THEN t.BracketSize ELSE t.BracketSize - 1 END AS ExpectedGames,
    COUNT(DISTINCT g.TemplateGameId)        AS Games,
    CASE WHEN t.BracketSize >= 4 THEN t.BracketSize ELSE t.BracketSize - 2 END AS ExpectedRoutes,
    COUNT(DISTINCT r.AdvancementRouteId)    AS Routes,
    SUM(CASE WHEN g.RoundType = 'B' THEN 1 ELSE 0 END) AS BronzeGames  -- 1 for size>=4, 0 for size 2
FROM brackets.Templates t
JOIN brackets.Strategies s ON s.StrategyId = t.StrategyId AND s.Code = 'SE'
LEFT JOIN brackets.TemplateGames g ON g.TemplateId = t.TemplateId
LEFT JOIN brackets.AdvancementRoutes r ON r.SourceTemplateGameId = g.TemplateGameId
GROUP BY t.BracketSize
ORDER BY t.BracketSize;
GO

-- First-round leaf pairings for the size-16 template
-- (eyeball vs canonical: 1-16, 8-9, 4-13, 5-12, 2-15, 7-10, 3-14, 6-11)
SELECT g.GameKey, g.Slot1Seed, g.Slot2Seed
FROM brackets.TemplateGames g
JOIN brackets.Templates t ON t.TemplateId = g.TemplateId
JOIN brackets.Strategies s ON s.StrategyId = t.StrategyId AND s.Code = 'SE'
WHERE t.BracketSize = 16 AND g.RoundType = 'X'
ORDER BY g.GameKey;
GO

-- Bronze wiring for size-16: both semis' LOSERS feed the optional 'B' game
SELECT src.RoundType AS SrcRound, src.GameKey AS SrcGame, r.SourceResult,
       tgt.RoundType AS TgtRound, r.TargetSlot, tgt.IsOptional
FROM brackets.AdvancementRoutes r
JOIN brackets.TemplateGames src ON src.TemplateGameId = r.SourceTemplateGameId
JOIN brackets.TemplateGames tgt ON tgt.TemplateGameId = r.TargetTemplateGameId
JOIN brackets.Templates t ON t.TemplateId = src.TemplateId
JOIN brackets.Strategies s ON s.StrategyId = t.StrategyId AND s.Code = 'SE'
WHERE t.BracketSize = 16 AND tgt.RoundType = 'B'
ORDER BY src.GameKey;
GO
