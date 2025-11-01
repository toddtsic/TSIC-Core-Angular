-- Find jobs with player profiles for migration testing
-- Run this in SQL Server Management Studio against your TSICV5 database

-- 1. Find jobs with CoreRegformPlayer settings
SELECT TOP 20
    JobId, 
    JobName, 
    CoreRegformPlayer,
    CASE 
        WHEN PlayerProfileMetadataJson IS NULL THEN 'Not Migrated'
        ELSE 'Already Migrated'
    END AS MigrationStatus,
    LEN(PlayerProfileMetadataJson) AS MetadataSize,
    Modified
FROM Jobs
WHERE CoreRegformPlayer IS NOT NULL 
    AND CoreRegformPlayer != '0' 
    AND CoreRegformPlayer != '1'
    AND CoreRegformPlayer LIKE 'PP%'  -- Only player profiles
ORDER BY Modified DESC;

-- 2. Group by profile type to see distribution
SELECT 
    LEFT(CoreRegformPlayer, CHARINDEX('|', CoreRegformPlayer + '|') - 1) AS ProfileType,
    COUNT(*) AS JobCount,
    SUM(CASE WHEN PlayerProfileMetadataJson IS NULL THEN 1 ELSE 0 END) AS NotMigrated,
    SUM(CASE WHEN PlayerProfileMetadataJson IS NOT NULL THEN 1 ELSE 0 END) AS AlreadyMigrated
FROM Jobs
WHERE CoreRegformPlayer IS NOT NULL 
    AND CoreRegformPlayer != '0' 
    AND CoreRegformPlayer != '1'
    AND CoreRegformPlayer LIKE 'PP%'
GROUP BY LEFT(CoreRegformPlayer, CHARINDEX('|', CoreRegformPlayer + '|') - 1)
ORDER BY JobCount DESC;

-- 3. Find a specific PP10 job for testing
SELECT TOP 1
    JobId, 
    JobName, 
    CoreRegformPlayer,
    JsonOptions,
    PlayerProfileMetadataJson
FROM Jobs
WHERE CoreRegformPlayer LIKE 'PP10|%'
ORDER BY Modified DESC;

-- 4. Check existing JsonOptions structure for a job
SELECT TOP 1
    JobId,
    JobName,
    JsonOptions
FROM Jobs
WHERE JsonOptions IS NOT NULL
    AND LEN(JsonOptions) > 10
ORDER BY Modified DESC;

-- 5. Sample query to verify migration worked
-- Replace 'YOUR-JOB-GUID' with actual JobId from preview results
/*
SELECT 
    JobId,
    JobName,
    CoreRegformPlayer,
    PlayerProfileMetadataJson,
    LEN(PlayerProfileMetadataJson) AS MetadataLength,
    JSON_VALUE(PlayerProfileMetadataJson, '$.source.sourceFile') AS SourceFile,
    JSON_VALUE(PlayerProfileMetadataJson, '$.source.commitSha') AS CommitSha,
    JSON_VALUE(PlayerProfileMetadataJson, '$.source.migratedAt') AS MigratedAt,
    (SELECT COUNT(*) FROM OPENJSON(PlayerProfileMetadataJson, '$.fields')) AS FieldCount
FROM Jobs
WHERE JobId = 'YOUR-JOB-GUID';
*/
