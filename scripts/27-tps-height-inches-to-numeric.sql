/*
    27 — TPS Fall 144 Showcase: height dropdown becomes real inches
    Job: The Players Series:Fall 144 Showcase 2026  (A02924A9-6BE1-46FD-86CA-B6561573BDAB)

    WHAT IS WRONG
    Every player's height lives in ONE column, Jobs.Registrations.height_inches, shared across
    every event that player ever registers for. Across the platform that column holds 123,523
    rows of plain inches (53..84) and 4,000 rows of feet-dash-inches ("5-10"), because two
    kinds of form write to it in two different units.

    This job is the only LIVE job on the wrong side of that split. Its JsonOptions.List_HeightInches
    offers 5-0 .. 6-10 and stores whatever was picked verbatim, so a player who registers here
    gets "5-0" saved as their height. Of 66 live jobs that collect height, 65 render it as a
    typed number and validate it as one:

        PP35 (this job)                       SELECT of 5-0 .. 6-10      1 job
        PP07/PP20/PP37/PP49/PP52/PP55         TEXT, numeric              65 jobs

    So the player then registers for, say, American Select, that form prefills their saved
    height, and the browser will not render "5-0" inside <input type="number">. The field looks
    EMPTY while the model holds "5-0", and the form says "Must be a number" about a box the
    registrant cannot see anything wrong with. Repro is two clicks: register here, pick any
    height, then register the same player on any American Select job.

    WHY THE LIST AND NOT THE VALIDATOR
    The validator is right — a field labelled "HEIGHT (IN INCHES)" should hold a number. It is
    this job's option list that is lying: it is labelled inches and offers feet-dash-inches.

    WHY NOT KEEP "5-0" AS THE LABEL AND STORE 60
    That is the better form and the backend already supports it — ProfileMetadataService reads
    Text and Value separately, so {"Text":"5-0","Value":"60"} survives to the browser intact.
    The frontend then throws Text away: form-schema.service.ts:111 maps each option down to its
    Value alone and the template renders <option [value]="opt">{{ opt }}</option>, the same
    string twice. Until that is carried through as {value,label}, a Text that differs from its
    Value is simply not displayed — the registrant would see "60" regardless. Chosen remedy is
    therefore plain inches in both fields. The label/value split is filed separately.

    SORT ORDER COMES ALONG FOR FREE
    The current list also renders out of order — 5-0, 5-1, 5-10, 5-11, 5-2 ... — because the
    values are stored string-sorted and nothing in the read or render path re-sorts them.
    form-schema.service.ts:124 DOES sort numerically, but only when every option parses as a
    number, which "5-0" never does. Numeric inches make that sort fire, so this one edit fixes
    the ordering as well. Do not also hand-order the array expecting that to be what shows.

    WHAT THIS SCRIPT CHANGES
    PART 1 replaces Jobs.Jobs.JsonOptions -> $.List_HeightInches on this ONE job with 60..82
    (23 entries, same count as the 23 it replaces: 5-0 is 60 inches, 6-10 is 82). Every other
    key inside JsonOptions is untouched — JSON_MODIFY rewrites only that path.

    PlayerProfileMetadataJson is deliberately NOT touched. Its baked heightInches options are a
    migration snapshot; both the backend (ProfileMetadataService.EnrichOptionsFromJson) and the
    frontend (form-schema.service.ts:107, "the shared option set is authoritative") override
    them from JsonOptions whenever that set is non-empty. Editing both would create a second
    source of truth for the same list.

    Six other live jobs carry an identical 23-entry List_HeightInches with the same bad order —
    four Top Threat (PP45), two Blue Ridge Bombers (PP01), all clone inheritance. None of them
    has heightInches on its player form, so none can display or write it. They are left alone
    on purpose: the list is inert there, and removing config a form does not read is a separate
    audit, not a side effect of this fix.

    PART 2 is the one existing row. This job has 7 registrations; exactly one has a height, the
    "5-0" created during testing on 2026-09-17. No customer data is involved. It is left as a
    SELECT plus a commented UPDATE — run the SELECT, confirm it is still the single test row,
    then decide. The 4,000 feet-dash rows on OTHER jobs are NOT in scope here and are not
    converted by anything in this file.

    SAFE TO RE-RUN. PART 1 is idempotent — it rewrites the path to the same value.
*/

SET NOCOUNT ON;
DECLARE @jobID UNIQUEIDENTIFIER = 'A02924A9-6BE1-46FD-86CA-B6561573BDAB';

-- ─────────────────────────────────────────────────────────────────────────────
-- PART 1 — replace the option list
-- ─────────────────────────────────────────────────────────────────────────────

PRINT '--- BEFORE ---';
SELECT JobName,
       JSON_QUERY(JsonOptions, '$.List_HeightInches') AS List_HeightInches_Before,
       LEN(JsonOptions)                               AS JsonOptions_Len_Before
FROM Jobs.Jobs
WHERE jobID = @jobID;

-- 60..82 inches = 5'0" .. 6'10", the same span the 23 replaced entries covered.
DECLARE @newList NVARCHAR(MAX) = N'[' +
    N'{"Text":"60","Value":"60"},{"Text":"61","Value":"61"},{"Text":"62","Value":"62"},' +
    N'{"Text":"63","Value":"63"},{"Text":"64","Value":"64"},{"Text":"65","Value":"65"},' +
    N'{"Text":"66","Value":"66"},{"Text":"67","Value":"67"},{"Text":"68","Value":"68"},' +
    N'{"Text":"69","Value":"69"},{"Text":"70","Value":"70"},{"Text":"71","Value":"71"},' +
    N'{"Text":"72","Value":"72"},{"Text":"73","Value":"73"},{"Text":"74","Value":"74"},' +
    N'{"Text":"75","Value":"75"},{"Text":"76","Value":"76"},{"Text":"77","Value":"77"},' +
    N'{"Text":"78","Value":"78"},{"Text":"79","Value":"79"},{"Text":"80","Value":"80"},' +
    N'{"Text":"81","Value":"81"},{"Text":"82","Value":"82"}]';

IF ISJSON(@newList) <> 1
BEGIN
    RAISERROR('New List_HeightInches is not valid JSON — aborting.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

    UPDATE Jobs.Jobs
    SET    JsonOptions = JSON_MODIFY(JsonOptions, '$.List_HeightInches', JSON_QUERY(@newList))
    WHERE  jobID = @jobID
      AND  JsonOptions IS NOT NULL;

    IF @@ROWCOUNT <> 1
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR('Expected exactly 1 job row updated — rolled back.', 16, 1);
        RETURN;
    END

    -- Guard: the rewrite must leave the document valid and every other key in place.
    IF EXISTS (
        SELECT 1 FROM Jobs.Jobs
        WHERE jobID = @jobID
          AND (ISJSON(JsonOptions) <> 1
               OR JSON_QUERY(JsonOptions, '$.List_Positions')  IS NULL
               OR JSON_QUERY(JsonOptions, '$.List_GradYears')  IS NULL
               OR JSON_QUERY(JsonOptions, '$.ListSizes_Shorts') IS NULL)
    )
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR('JsonOptions lost sibling keys or is no longer valid JSON — rolled back.', 16, 1);
        RETURN;
    END

COMMIT TRANSACTION;

PRINT '--- AFTER ---';
SELECT JobName,
       JSON_QUERY(JsonOptions, '$.List_HeightInches') AS List_HeightInches_After,
       LEN(JsonOptions)                               AS JsonOptions_Len_After
FROM Jobs.Jobs
WHERE jobID = @jobID;

-- Sibling keys, eyeball check — these must read exactly as they did before.
SELECT JSON_QUERY(JsonOptions, '$.List_Positions')   AS Positions,
       JSON_QUERY(JsonOptions, '$.List_GradYears')   AS GradYears,
       JSON_QUERY(JsonOptions, '$.List_StrongHand')  AS StrongHand,
       JSON_QUERY(JsonOptions, '$.ListSizes_Shorts') AS ShortsSizes,
       JSON_QUERY(JsonOptions, '$.ListSizes_Tshirt') AS TshirtSizes,
       JSON_QUERY(JsonOptions, '$.List_RecruitingGradYears') AS RecruitingGradYears
FROM Jobs.Jobs
WHERE jobID = @jobID;


-- ─────────────────────────────────────────────────────────────────────────────
-- PART 2 — the one stored feet-dash height on this job (REVIEW, THEN DECIDE)
-- ─────────────────────────────────────────────────────────────────────────────
-- Expect a single row, the 2026-09-17 test registration holding '5-0'. If anything
-- else appears, STOP: a real registrant has entered a height and the conversion is
-- no longer a test cleanup.

SELECT r.RegistrationID,
       u.FirstName, u.LastName,
       '[' + ISNULL(r.height_inches, 'NULL') + ']' AS Height_Stored,
       r.RegistrationTS,
       r.bActive
FROM   Jobs.Registrations r
LEFT   JOIN dbo.AspNetUsers u ON u.Id = r.UserId
WHERE  r.jobID = @jobID
  AND  r.height_inches IS NOT NULL
  AND  LTRIM(RTRIM(r.height_inches)) <> ''
ORDER  BY r.RegistrationTS DESC;

/*  Only after the SELECT above shows nothing but the test row.
    Converts feet-dash to inches: '5-0' -> 60.  Leaves every other job alone.

UPDATE r
SET    r.height_inches = CAST(
           (TRY_CAST(LEFT(r.height_inches, CHARINDEX('-', r.height_inches) - 1) AS INT) * 12)
         + TRY_CAST(SUBSTRING(r.height_inches, CHARINDEX('-', r.height_inches) + 1, 10) AS INT)
           AS VARCHAR(10))
FROM   Jobs.Registrations r
WHERE  r.jobID = @jobID
  AND  r.height_inches LIKE '%[0-9]-[0-9]%'
  AND  TRY_CAST(LEFT(r.height_inches, CHARINDEX('-', r.height_inches) - 1) AS INT) IS NOT NULL
  AND  TRY_CAST(SUBSTRING(r.height_inches, CHARINDEX('-', r.height_inches) + 1, 10) AS INT) IS NOT NULL;
*/
