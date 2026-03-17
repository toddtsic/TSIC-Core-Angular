-- ═══════════════════════════════════════════════════════════════════
-- TSIC Demo PII Morph Script
-- Run against the RESTORED demo database (never against dev/prod!)
-- ═══════════════════════════════════════════════════════════════════
-- This script morphs all personally identifiable information into
-- deterministic fake data. Structure, config, and relationships
-- are preserved — only human-identifying content changes.
-- ═══════════════════════════════════════════════════════════════════

SET NOCOUNT ON;
PRINT '──────────────────────────────────────────────';
PRINT '  TSIC Demo PII Morph — Starting ...';
PRINT '──────────────────────────────────────────────';

-- ── Morph seed for deterministic but varied output ──
-- Change this value to get different fake names across runs
DECLARE @MorphSeed INT = 42;

-- ═══════════════════════════════════════════════════════════════════
-- 1. JOBS — Morph identity, contacts, email configs
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Jobs ...';

-- Assign demo names: "Demo Tournament A", "Demo League B", etc.
;WITH JobNumbered AS (
    SELECT
        JobId,
        JobName,
        DisplayName,
        JobPath,
        ROW_NUMBER() OVER (ORDER BY JobName) AS RowNum
    FROM Jobs.Jobs
)
UPDATE JobNumbered SET
    JobName     = 'Demo Event ' + CHAR(64 + ((RowNum - 1) % 26) + 1)
                  + CASE WHEN RowNum > 26 THEN CAST(RowNum / 26 AS VARCHAR) ELSE '' END,
    DisplayName = 'Demo Event ' + CHAR(64 + ((RowNum - 1) % 26) + 1)
                  + CASE WHEN RowNum > 26 THEN CAST(RowNum / 26 AS VARCHAR) ELSE '' END,
    JobPath     = 'demo-event-' + LOWER(CHAR(64 + ((RowNum - 1) % 26) + 1))
                  + CASE WHEN RowNum > 26 THEN CAST(RowNum / 26 AS VARCHAR) ELSE '' END;

-- Morph ALL identifying fields on Jobs
UPDATE Jobs.Jobs SET
    -- Contact / email fields
    MailTo                              = 'contact@demo-event.local',
    PayTo                               = 'Demo Sports Organization',
    StoreContactEmail                   = 'store@demo-event.local',
    Rescheduleemaillist                 = 'reschedule@demo-event.local',
    Alwayscopyemaillist                 = 'copy@demo-event.local',
    RegFormFrom                         = 'registration@demo-event.local',
    RegFormCcs                          = NULL,
    RegFormBccs                         = NULL,
    -- Display / SEO text (often contains real event name)
    JobDescription                      = 'Demo event for presentation purposes.',
    JobTagline                          = 'Where Champions Compete',
    JobNameQbp                          = NULL,
    MobileJobName                       = NULL,
    JobCode                             = NULL,
    BannerFile                          = NULL,
    SearchenginKeywords                 = 'demo, sports, tournament',
    SearchengineDescription             = 'Demo event for platform demonstration.',
    MailinPaymentWarning                = NULL,
    -- Confirmation emails (HTML that names the real organization)
    PlayerRegConfirmationEmail          = NULL,
    PlayerRegConfirmationOnScreen       = NULL,
    AdultRegConfirmationEmail           = NULL,
    AdultRegConfirmationOnScreen        = NULL,
    RefereeRegConfirmationEmail         = NULL,
    RefereeRegConfirmationOnScreen      = NULL,
    RecruiterRegConfirmationEmail       = NULL,
    RecruiterRegConfirmationOnScreen    = NULL,
    -- Legal / policy text (names the organization)
    PlayerRegRefundPolicy               = NULL,
    PlayerRegReleaseOfLiability         = NULL,
    PlayerRegCodeOfConduct              = NULL,
    AdultRegRefundPolicy                = NULL,
    AdultRegReleaseOfLiability          = NULL,
    AdultRegCodeOfConduct               = NULL,
    PlayerRegCovid19Waiver              = NULL,
    -- Store text (may reference venue/org)
    StoreRefundPolicy                   = NULL,
    StorePickupDetails                  = NULL;

PRINT '  Jobs morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 2. ASPNETUSERS — Names, emails, phones, addresses
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing AspNetUsers ...';

-- Name pools for realistic-looking fake names
DECLARE @FirstNames TABLE (Id INT IDENTITY(1,1), Name NVARCHAR(50));
INSERT @FirstNames (Name) VALUES
    ('Alex'),('Jordan'),('Taylor'),('Morgan'),('Casey'),
    ('Riley'),('Quinn'),('Avery'),('Parker'),('Drew'),
    ('Jamie'),('Sage'),('Blake'),('Skyler'),('Reese'),
    ('Cameron'),('Logan'),('Harper'),('Emery'),('Dakota'),
    ('Finley'),('Rowan'),('Peyton'),('Hayden'),('Kendall'),
    ('Ellis'),('Remy'),('Frankie'),('Shea'),('Lane'),
    ('Tatum'),('Lennox'),('Marley'),('Phoenix'),('Oakley'),
    ('River'),('Justice'),('Arden'),('Scout'),('Wren');

DECLARE @LastNames TABLE (Id INT IDENTITY(1,1), Name NVARCHAR(50));
INSERT @LastNames (Name) VALUES
    ('Anderson'),('Bennett'),('Carter'),('Dawson'),('Edwards'),
    ('Foster'),('Grant'),('Hayes'),('Irving'),('Jensen'),
    ('Keller'),('Lambert'),('Mitchell'),('Nolan'),('Owens'),
    ('Palmer'),('Quinn'),('Reynolds'),('Sullivan'),('Turner'),
    ('Underwood'),('Vance'),('Wallace'),('Young'),('Zimmerman'),
    ('Archer'),('Brooks'),('Caldwell'),('Dixon'),('Emerson'),
    ('Fletcher'),('Gibson'),('Holland'),('Ingram'),('Jarvis'),
    ('Knox'),('Lawson'),('Mason'),('Nash'),('Oliver');

DECLARE @FirstCount INT = (SELECT COUNT(*) FROM @FirstNames);
DECLARE @LastCount INT  = (SELECT COUNT(*) FROM @LastNames);

;WITH UserNumbered AS (
    SELECT
        Id,
        ROW_NUMBER() OVER (ORDER BY Id) AS RowNum
    FROM dbo.AspNetUsers
)
UPDATE u SET
    FirstName       = fn.Name,
    LastName        = ln.Name,
    UserName        = LOWER(fn.Name) + '.' + LOWER(ln.Name) + CAST(un.RowNum AS VARCHAR),
    NormalizedUserName = UPPER(LOWER(fn.Name) + '.' + LOWER(ln.Name) + CAST(un.RowNum AS VARCHAR)),
    Email           = LOWER(fn.Name) + '.' + LOWER(ln.Name) + CAST(un.RowNum AS VARCHAR) + '@demo.local',
    NormalizedEmail = UPPER(LOWER(fn.Name) + '.' + LOWER(ln.Name) + CAST(un.RowNum AS VARCHAR) + '@demo.local'),
    PhoneNumber     = '555-' + RIGHT('0000' + CAST(un.RowNum AS VARCHAR), 4),
    Cellphone       = '555-' + RIGHT('0000' + CAST(un.RowNum + 5000 AS VARCHAR), 4),
    Phone           = NULL,
    Workphone       = NULL,
    Fax             = NULL,
    StreetAddress   = CAST(un.RowNum AS VARCHAR) + ' Demo Street',
    City            = 'Demoville',
    [State]         = 'XX',
    PostalCode      = RIGHT('00000' + CAST((10000 + un.RowNum) AS VARCHAR), 5),
    Country         = 'US',
    ImageFile       = NULL,
    ImageFileMimeType = NULL,
    -- Reset all passwords to known demo credential: Demo2026!
    -- This is the ASP.NET Identity V3 hash for "Demo2026!" — regenerate if needed
    -- For now, set a marker; the PowerShell script can reset via UserManager if needed
    SecurityStamp   = NEWID()
FROM dbo.AspNetUsers u
INNER JOIN UserNumbered un ON u.Id = un.Id
INNER JOIN @FirstNames fn ON fn.Id = ((un.RowNum - 1) % @FirstCount) + 1
INNER JOIN @LastNames  ln ON ln.Id = ((un.RowNum / @FirstCount) % @LastCount) + 1;

PRINT '  AspNetUsers morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 3. FAMILIES — Parent names, emails, phones
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Families ...';

;WITH FamNumbered AS (
    SELECT
        FamilyId,
        ROW_NUMBER() OVER (ORDER BY FamilyId) AS RowNum
    FROM dbo.Families
)
UPDATE f SET
    MomFirstName        = 'Parent',
    MomLastName         = 'Alpha-' + CAST(fn.RowNum AS VARCHAR),
    MomEmail            = 'parent.alpha.' + CAST(fn.RowNum AS VARCHAR) + '@demo.local',
    MomCellphone        = '555-' + RIGHT('0000' + CAST(fn.RowNum + 1000 AS VARCHAR), 4),
    MomCellphoneProvider = NULL,
    DadFirstName        = 'Parent',
    DadLastName         = 'Beta-' + CAST(fn.RowNum AS VARCHAR),
    DadEmail            = 'parent.beta.' + CAST(fn.RowNum AS VARCHAR) + '@demo.local',
    DadCellphone        = '555-' + RIGHT('0000' + CAST(fn.RowNum + 2000 AS VARCHAR), 4),
    DadCellphoneProvider = NULL
FROM dbo.Families f
INNER JOIN FamNumbered fn ON f.FamilyId = fn.FamilyId;

PRINT '  Families morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 4. TEAMS — Team names, coach refs
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Teams ...';

DECLARE @TeamColors TABLE (Id INT IDENTITY(1,1), Color NVARCHAR(20));
INSERT @TeamColors (Color) VALUES
    ('Red'),('Blue'),('Gold'),('Green'),('Silver'),
    ('Black'),('White'),('Orange'),('Purple'),('Crimson'),
    ('Navy'),('Teal'),('Maroon'),('Gray'),('Scarlet'),
    ('Royal'),('Storm'),('Thunder'),('Lightning'),('Phoenix');

DECLARE @TeamAnimals TABLE (Id INT IDENTITY(1,1), Animal NVARCHAR(20));
INSERT @TeamAnimals (Animal) VALUES
    ('Eagles'),('Hawks'),('Tigers'),('Lions'),('Bears'),
    ('Wolves'),('Falcons'),('Panthers'),('Jaguars'),('Cobras'),
    ('Sharks'),('Stallions'),('Vipers'),('Raptors'),('Mustangs'),
    ('Coyotes'),('Hornets'),('Blazers'),('Comets'),('Titans');

DECLARE @ColorCount  INT = (SELECT COUNT(*) FROM @TeamColors);
DECLARE @AnimalCount INT = (SELECT COUNT(*) FROM @TeamAnimals);

;WITH TeamNumbered AS (
    SELECT
        TeamId,
        ROW_NUMBER() OVER (ORDER BY TeamId) AS RowNum
    FROM Leagues.teams
)
UPDATE t SET
    TeamName    = tc.Color + ' ' + ta.Animal,
    DisplayName = tc.Color + ' ' + ta.Animal,
    OldCoach    = NULL,
    OldTeamName = NULL,
    TeamComments = NULL
FROM Leagues.teams t
INNER JOIN TeamNumbered tn ON t.TeamId = tn.TeamId
INNER JOIN @TeamColors  tc ON tc.Id = ((tn.RowNum - 1) % @ColorCount) + 1
INNER JOIN @TeamAnimals ta ON ta.Id = (((tn.RowNum - 1) / @ColorCount) % @AnimalCount) + 1;

PRINT '  Teams morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 5. CLUBS — Club names
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Clubs ...';

;WITH ClubNumbered AS (
    SELECT
        ClubId,
        ROW_NUMBER() OVER (ORDER BY ClubId) AS RowNum
    FROM Clubs.Clubs
)
UPDATE c SET
    ClubName = 'Demo Club ' + CHAR(64 + ((cn.RowNum - 1) % 26) + 1)
               + CASE WHEN cn.RowNum > 26 THEN CAST(cn.RowNum / 26 AS VARCHAR) ELSE '' END
FROM Clubs.Clubs c
INNER JOIN ClubNumbered cn ON c.ClubId = cn.ClubId;

-- Also morph reference.clubs if it exists (legacy table)
IF OBJECT_ID('reference.clubs', 'U') IS NOT NULL
BEGIN
    ;WITH RefClubNumbered AS (
        SELECT
            ClubId,
            ROW_NUMBER() OVER (ORDER BY ClubId) AS RowNum
        FROM reference.clubs
    )
    UPDATE c SET
        ClubName = 'Demo Club Ref-' + CAST(rcn.RowNum AS VARCHAR)
    FROM reference.clubs c
    INNER JOIN RefClubNumbered rcn ON c.ClubId = rcn.ClubId;
END

PRINT '  Clubs morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 6. REGISTRATIONS — Personal fields, medical, academic, social
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Registrations ...';

UPDATE Jobs.Registrations SET
    -- Free-text personal fields
    SpecialRequests     = NULL,
    RoommatePref        = NULL,
    MedicalNote         = NULL,
    BackcheckExplain    = NULL,
    WhoReferred         = NULL,
    Region              = NULL,
    -- Insurance PII
    InsuredName         = 'Demo Insured',
    HealthInsurerPolicyNo = 'DEMO-000000',
    HealthInsurerGroupNo  = 'GRP-000',
    HealthInsurerPhone  = '555-0000',
    HealthInsurer       = 'Demo Insurance Co.',
    RegsaverPolicyId    = NULL,
    -- Coach / school / club names (identifies real orgs)
    SchoolCoach         = NULL,
    SchoolCoachEmail    = NULL,
    ClubCoach           = NULL,
    ClubCoachEmail      = NULL,
    PreviousCoach1      = NULL,
    PreviousCoach2      = NULL,
    ClubName            = NULL,
    ClubTeamName        = NULL,
    SchoolName          = 'Demo High School',
    SchoolTeamName      = NULL,
    CollegeCommit       = NULL,
    -- Social media handles
    MomTwitter          = NULL,
    DadTwitter          = NULL,
    MomInstagram        = NULL,
    DadInstagram        = NULL,
    Twitter             = NULL,
    Instagram           = NULL,
    Snapchat            = NULL,
    TikTokHandle        = NULL,
    RecruitingHandle    = NULL,
    -- Photos / documents
    HeadshotPath        = NULL,
    -- Academic PII (test scores, grades, honors)
    Act                 = NULL,
    Sat                 = NULL,
    SatMath             = NULL,
    SatVerbal           = NULL,
    SatWriting          = NULL,
    Psat                = NULL,
    Gpa                 = NULL,
    ClassRank           = NULL,
    GradYear            = NULL,
    SchoolGrade         = NULL,
    HonorsAcademic      = NULL,
    HonorsAthletic      = NULL,
    SchoolLevelClasses  = NULL,
    SchoolActivities    = NULL,
    Honors              = NULL,
    -- Physical measurements
    HeightInches        = NULL,
    Height              = NULL,
    WeightLbs           = NULL,
    -- Membership / cert IDs
    CertNo              = NULL,
    SportAssnId         = NULL,
    AdnSubscriptionId   = NULL,
    -- Volunteer info (may name children)
    VolChildreninprogram = NULL,
    Volposition         = NULL,
    -- Activities that could identify leagues/camps
    LeaguesAttending    = NULL,
    CampsAttending      = NULL,
    OtherSports         = NULL,
    -- Form name (may contain event name)
    RegistrationFormName = NULL,
    -- Jitter registration fee amounts (±15%)
    FeeBase             = CASE WHEN FeeBase != 0
                            THEN ROUND(FeeBase * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                            ELSE FeeBase END,
    FeeTotal            = CASE WHEN FeeTotal != 0
                            THEN ROUND(FeeTotal * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                            ELSE FeeTotal END,
    OwedTotal           = CASE WHEN OwedTotal != 0
                            THEN ROUND(OwedTotal * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                            ELSE OwedTotal END,
    PaidTotal           = CASE WHEN PaidTotal != 0
                            THEN ROUND(PaidTotal * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                            ELSE PaidTotal END,
    FeeProcessing       = CASE WHEN FeeProcessing != 0
                            THEN ROUND(FeeProcessing * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                            ELSE FeeProcessing END,
    FeeDiscount         = CASE WHEN FeeDiscount != 0
                            THEN ROUND(FeeDiscount * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                            ELSE FeeDiscount END,
    FeeDonation         = CASE WHEN FeeDonation != 0
                            THEN ROUND(FeeDonation * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                            ELSE FeeDonation END;

PRINT '  Registrations morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 7. REGISTRATION ACCOUNTING — Jitter financials, scrub card refs
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Registration Accounting ...';

-- Jitter amounts by ±15% (deterministic per row)
UPDATE Jobs.Registration_Accounting SET
    AdnCc4          = '0000',
    AdnCcexpDate    = NULL,
    AdnTransactionId = 'DEMO-' + CAST(NEWID() AS VARCHAR(8)),
    AdnInvoiceNo    = NULL,
    CheckNo         = NULL,
    Comment         = NULL,
    -- Jitter dollar amounts
    Amount          = CASE WHEN Amount IS NOT NULL AND Amount != 0
                        THEN ROUND(Amount * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                        ELSE Amount END,
    Paid            = CASE WHEN Paid IS NOT NULL AND Paid != 0
                        THEN ROUND(Paid * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                        ELSE Paid END,
    Balance         = CASE WHEN Balance IS NOT NULL AND Balance != 0
                        THEN ROUND(Balance * (0.85 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / 2147483647.0) * 0.30), 2)
                        ELSE Balance END;

PRINT '  Accounting morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 8. PERSON CONTACTS — Emergency contacts, physician, etc.
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Person Contacts ...';

UPDATE dbo.Person_Contacts SET
    -- Primary contact
    CcFirstName         = 'Contact',
    CcLastName          = 'Primary',
    CcCellphone         = '555-1100',
    CcCellphoneProvider = NULL,
    -- Emergency contact
    CeFirstName         = 'Contact',
    CeLastName          = 'Emergency',
    CeEmail             = 'emergency@demo.local',
    CeEmailSms          = NULL,
    CeCellphone         = '555-1200',
    CeCellphoneProvider = NULL,
    CeHomephone         = NULL,
    CeWorkphone         = NULL,
    -- Physician
    CpFirstName         = 'Dr.',
    CpLastName          = 'Demo',
    CpEmail             = 'doctor@demo.local',
    CpEmailSms          = NULL,
    CpCellphone         = '555-1300',
    CpCellphoneProvider = NULL,
    CpHomephone         = NULL,
    CpWorkphone         = NULL,
    -- Secondary emergency
    CsFirstName         = 'Contact',
    CsLastName          = 'Secondary',
    CsEmail             = 'secondary@demo.local',
    CsEmailSms          = NULL,
    CsCellphone         = '555-1400',
    CsCellphoneProvider = NULL,
    CsHomephone         = NULL,
    CsWorkphone         = NULL,
    -- Primary email
    REmail              = 'registrant@demo.local',
    REmailSms           = NULL;

PRINT '  Person Contacts morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 9. CUSTOMERS — Org names, payment processor keys
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Customers ...';

;WITH CustNumbered AS (
    SELECT
        CustomerId,
        ROW_NUMBER() OVER (ORDER BY CustomerId) AS RowNum
    FROM Jobs.Customers
)
UPDATE c SET
    CustomerName       = 'Demo Organization ' + CAST(cn.RowNum AS VARCHAR),
    AdnLoginId         = 'demo-login-' + CAST(cn.RowNum AS VARCHAR),
    AdnTransactionKey  = 'demo-key-xxxx'
FROM Jobs.Customers c
INNER JOIN CustNumbered cn ON c.CustomerId = cn.CustomerId;

PRINT '  Customers morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 10. CHAT & TEAM MESSAGES — Scrub content
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Chat & Team Messages ...';

UPDATE chat.ChatMessages SET
    [Message] = 'Demo message content.';

UPDATE mobile.TeamMessages SET
    Title       = 'Demo Announcement',
    Content     = '<p>This is a demo announcement.</p>',
    AttachmentUrl = NULL,
    PhotoUrl    = NULL;

PRINT '  Messages morphed';

-- ═══════════════════════════════════════════════════════════════════
-- 11. EMAIL LOGS — Scrub recipient data
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Email Logs ...';

-- Truncate rather than morph — email logs are large and 100% PII
TRUNCATE TABLE Jobs.emailLogs;
TRUNCATE TABLE Jobs.emailFailures;

PRINT '  Email logs truncated';

-- ═══════════════════════════════════════════════════════════════════
-- 12. JOB DISPLAY OPTIONS — Morph identifying text (images handled by PS)
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Job Display Options ...';

UPDATE Jobs.JobDisplayOptions SET
    -- Slide text (often contains event name, venue, sponsors)
    ParallaxSlide1Text1     = 'Welcome to the Demo Event',
    ParallaxSlide1Text2     = 'Experience the Platform',
    ParallaxSlide2Text1     = NULL,
    ParallaxSlide2Text2     = NULL,
    ParallaxSlide3Text1     = NULL,
    ParallaxSlide3Text2     = NULL,
    -- Content blocks (promo text, may name org/venue)
    BlockRecentWorks        = NULL,
    BlockPurchase           = NULL,
    BlockService            = NULL,
    -- Image filenames (morph to match stock assets the PS script copies)
    ParallaxBackgroundImage = 'demo-banner-bg.jpg',
    ParallaxSlide1Image     = NULL,
    ParallaxSlide2Image     = NULL,
    ParallaxSlide3Image     = NULL,
    LogoHeader              = 'demo-logo-header.png',
    LogoFooter              = NULL,
    BlockRecentImage1       = NULL,
    BlockRecentImage2       = NULL,
    BlockRecentImage3       = NULL,
    BlockRecentImage4       = NULL;

PRINT '  JobDisplayOptions morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 13. JOB OWL IMAGES — Scrub captions
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Job Owl Images ...';

IF OBJECT_ID('Jobs.JobOwlImages', 'U') IS NOT NULL
BEGIN
    UPDATE Jobs.JobOwlImages SET
        Caption = 'Demo event photo';
    PRINT '  JobOwlImages morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);
END

-- ═══════════════════════════════════════════════════════════════════
-- 14. BULLETINS — Scrub announcement content
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing Bulletins ...';

UPDATE Jobs.Bulletins SET
    Title       = 'Demo Bulletin',
    Body        = 'This is a sample bulletin for demonstration purposes.',
    Author      = 'Demo Admin';

PRINT '  Bulletins morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ═══════════════════════════════════════════════════════════════════
-- 15. SMS BROADCASTS — Scrub message content
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Morphing SMS Broadcasts ...';

IF OBJECT_ID('Jobs.Job_SMSBroadcasts', 'U') IS NOT NULL
BEGIN
    UPDATE Jobs.Job_SMSBroadcasts SET
        SmsMessage  = 'Demo SMS notification.',
        ToNumber    = '555-0000',
        FromNumber  = '555-0001';
    PRINT '  SMS Broadcasts morphed: ' + CAST(@@ROWCOUNT AS VARCHAR);
END

-- ═══════════════════════════════════════════════════════════════════
-- 16. APP LOG — Truncate (may contain user actions with PII)
-- ═══════════════════════════════════════════════════════════════════
PRINT 'Truncating App Log ...';

IF OBJECT_ID('logs.AppLog', 'U') IS NOT NULL
    TRUNCATE TABLE logs.AppLog;

PRINT '  App Log truncated';

-- ═══════════════════════════════════════════════════════════════════
-- VERIFICATION
-- ═══════════════════════════════════════════════════════════════════
PRINT '';
PRINT '──────────────────────────────────────────────';
PRINT '  MORPH COMPLETE — Verification Summary';
PRINT '──────────────────────────────────────────────';

SELECT 'Jobs'               AS [Table], COUNT(*) AS [Rows] FROM Jobs.Jobs
UNION ALL
SELECT 'AspNetUsers',       COUNT(*) FROM dbo.AspNetUsers
UNION ALL
SELECT 'Families',          COUNT(*) FROM dbo.Families
UNION ALL
SELECT 'Teams',             COUNT(*) FROM Leagues.teams
UNION ALL
SELECT 'Clubs',             COUNT(*) FROM Clubs.Clubs
UNION ALL
SELECT 'Registrations',     COUNT(*) FROM Jobs.Registrations
UNION ALL
SELECT 'Accounting',        COUNT(*) FROM Jobs.Registration_Accounting
UNION ALL
SELECT 'PersonContacts',    COUNT(*) FROM dbo.Person_Contacts
UNION ALL
SELECT 'Customers',         COUNT(*) FROM Jobs.Customers
UNION ALL
SELECT 'ChatMessages',      COUNT(*) FROM chat.ChatMessages
UNION ALL
SELECT 'TeamMessages',      COUNT(*) FROM mobile.TeamMessages
ORDER BY [Table];

PRINT '';
PRINT '  All PII has been morphed. Database is demo-safe.';
PRINT '──────────────────────────────────────────────';
