/*
    27 — invites schema: invitation send tracking

    WHAT THIS DOES
    Creates a new `invites` schema holding the SEND side of registration invitations: who was
    invited, to which event, with what letter, expiring when, and whether the email got out.

    NOTHING IS WRITTEN AFTER THE SEND. Whether an invitation was taken up is inferred live by the
    search query, only when an invite filter is chosen:
        Player   — the invited player has a registration in the target event
        Club Rep — a team of the invited CLUB exists in the target event (any rep)
        Preview  — nothing to infer; status is "Sent", terminal
    Neither Registrations.bActive nor teams.active is consulted, and no date is compared against
    the invitation's expiry. Existence is success. See the design artifact for the full rules.

    WHAT IT TOUCHES
    Five new tables in a new schema, their indexes, and three seed lookups. NO existing table is
    altered. No triggers, no cascades, no existing index changed. Foreign keys are created FROM the
    new tables TO Jobs.Jobs, Jobs.Registrations and dbo.AspNetUsers — the same pattern those tables
    already carry 37, 21 and 97 times respectively, none of them cascading.

    Until application code ships, these tables are inert: nothing reads or writes them.

    RE-RUN SAFE
    Every object is guarded by an existence check, so a second run is a no-op and reports as much.
    Seed rows are inserted only when their id is missing; an edited name is never overwritten.

    RUN
    @Commit = 0 (default) creates everything inside a transaction, reports, then ROLLS BACK.
    @Commit = 1 commits.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Commit bit = 0;   -- <<< set to 1 to actually apply

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @created TABLE (Step nvarchar(120), Action nvarchar(20));

    ----------------------------------------------------------------------------------------------
    -- 1. Schema
    ----------------------------------------------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'invites')
    BEGIN
        EXEC ('CREATE SCHEMA [invites]');
        INSERT @created VALUES ('schema invites', 'created');
    END
    ELSE INSERT @created VALUES ('schema invites', 'already there');

    ----------------------------------------------------------------------------------------------
    -- 2. Lookups. Ids match the C# enums. No audit fields, matching reference.JobTypes.
    ----------------------------------------------------------------------------------------------
    IF OBJECT_ID('invites.InviteKinds') IS NULL
    BEGIN
        CREATE TABLE invites.InviteKinds (
            InviteKindId    int          NOT NULL CONSTRAINT PK_InviteKinds PRIMARY KEY CLUSTERED,
            InviteKindName  nvarchar(50) NOT NULL
        );
        INSERT @created VALUES ('invites.InviteKinds', 'created');
    END
    ELSE INSERT @created VALUES ('invites.InviteKinds', 'already there');

    IF OBJECT_ID('invites.InvitationOutcomes') IS NULL
    BEGIN
        CREATE TABLE invites.InvitationOutcomes (
            InvitationOutcomeId    int          NOT NULL CONSTRAINT PK_InvitationOutcomes PRIMARY KEY CLUSTERED,
            InvitationOutcomeName  nvarchar(50) NOT NULL
        );
        INSERT @created VALUES ('invites.InvitationOutcomes', 'created');
    END
    ELSE INSERT @created VALUES ('invites.InvitationOutcomes', 'already there');

    -- Not referenced by any stored column on purpose: status is DERIVED by the search query.
    -- These rows label the grid column AND fill the Invitations filter options.
    IF OBJECT_ID('invites.InviteStatuses') IS NULL
    BEGIN
        CREATE TABLE invites.InviteStatuses (
            InviteStatusId    int          NOT NULL CONSTRAINT PK_InviteStatuses PRIMARY KEY CLUSTERED,
            InviteStatusName  nvarchar(50) NOT NULL
        );
        INSERT @created VALUES ('invites.InviteStatuses', 'created');
    END
    ELSE INSERT @created VALUES ('invites.InviteStatuses', 'already there');

    ----------------------------------------------------------------------------------------------
    -- 3. Invitations — ONE ROW PER CLICK OF SEND.
    --    A reminder is a new Invitation with new InvitationRegistrations rows; attempts are the
    --    row count. Each send's links keep their own expiry, so ExpiresAt is never updated.
    ----------------------------------------------------------------------------------------------
    IF OBJECT_ID('invites.Invitations') IS NULL
    BEGIN
        CREATE TABLE invites.Invitations (
            InvitationId   uniqueidentifier NOT NULL CONSTRAINT DF_Invitations_InvitationId DEFAULT (newid())
                                            CONSTRAINT PK_Invitations PRIMARY KEY CLUSTERED,
            SourceJobId    uniqueidentifier NOT NULL,   -- event sent from
            TargetJobId    uniqueidentifier NOT NULL,   -- event invited to (= source for a preview)
            InviteKindId   int              NOT NULL,
            Subject        nvarchar(max)    NOT NULL,   -- as sent, tokens unfilled
            BodyTemplate   nvarchar(max)    NOT NULL,   -- as sent, tokens unfilled
            ExpiresAt      datetime         NOT NULL,   -- every link in this send expires here
            Modified       datetime         NOT NULL CONSTRAINT DF_Invitations_Modified DEFAULT (getdate()),
            LebUserId      nvarchar(450)    NOT NULL    -- sent by; never null, by ruling
        );
        INSERT @created VALUES ('invites.Invitations', 'created');
    END
    ELSE INSERT @created VALUES ('invites.Invitations', 'already there');

    ----------------------------------------------------------------------------------------------
    -- 4. InvitationRegistrations — ONE ROW PER INVITED PERSON, PER SEND.
    --    Clustered on (SourceRegistrationId, InvitationId) so every invite a registration ever
    --    received sits together and its invite count is one seek.
    --    For a club rep, SourceRegistrationId is also the CLUB key: it walks to Leagues.teams via
    --    clubrep_registrationid, then ClubTeamId -> Clubs.ClubTeams.ClubId. No club id is stored.
    ----------------------------------------------------------------------------------------------
    IF OBJECT_ID('invites.InvitationRegistrations') IS NULL
    BEGIN
        CREATE TABLE invites.InvitationRegistrations (
            SourceRegistrationId  uniqueidentifier NOT NULL,
            InvitationId          uniqueidentifier NOT NULL,
            InvitationOutcomeId   int              NOT NULL,  -- written once, at the send attempt
            Modified              datetime         NOT NULL CONSTRAINT DF_InvitationRegistrations_Modified DEFAULT (getdate()),
            LebUserId             nvarchar(450)    NOT NULL,
            CONSTRAINT PK_InvitationRegistrations PRIMARY KEY CLUSTERED (SourceRegistrationId, InvitationId)
        );
        INSERT @created VALUES ('invites.InvitationRegistrations', 'created');
    END
    ELSE INSERT @created VALUES ('invites.InvitationRegistrations', 'already there');

    ----------------------------------------------------------------------------------------------
    -- 5. Foreign keys. NO CASCADES — matching every existing FK on these parents.
    ----------------------------------------------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Invitations_InviteKinds')
    BEGIN
        ALTER TABLE invites.Invitations WITH CHECK
            ADD CONSTRAINT FK_Invitations_InviteKinds
            FOREIGN KEY (InviteKindId) REFERENCES invites.InviteKinds (InviteKindId);
        INSERT @created VALUES ('FK_Invitations_InviteKinds', 'created');
    END
    ELSE INSERT @created VALUES ('FK_Invitations_InviteKinds', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Invitations_SourceJob')
    BEGIN
        ALTER TABLE invites.Invitations WITH CHECK
            ADD CONSTRAINT FK_Invitations_SourceJob
            FOREIGN KEY (SourceJobId) REFERENCES Jobs.Jobs (jobID);
        INSERT @created VALUES ('FK_Invitations_SourceJob', 'created');
    END
    ELSE INSERT @created VALUES ('FK_Invitations_SourceJob', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Invitations_TargetJob')
    BEGIN
        ALTER TABLE invites.Invitations WITH CHECK
            ADD CONSTRAINT FK_Invitations_TargetJob
            FOREIGN KEY (TargetJobId) REFERENCES Jobs.Jobs (jobID);
        INSERT @created VALUES ('FK_Invitations_TargetJob', 'created');
    END
    ELSE INSERT @created VALUES ('FK_Invitations_TargetJob', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Invitations_LebUser')
    BEGIN
        ALTER TABLE invites.Invitations WITH CHECK
            ADD CONSTRAINT FK_Invitations_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers (Id);
        INSERT @created VALUES ('FK_Invitations_LebUser', 'created');
    END
    ELSE INSERT @created VALUES ('FK_Invitations_LebUser', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InvitationRegistrations_Invitations')
    BEGIN
        ALTER TABLE invites.InvitationRegistrations WITH CHECK
            ADD CONSTRAINT FK_InvitationRegistrations_Invitations
            FOREIGN KEY (InvitationId) REFERENCES invites.Invitations (InvitationId);
        INSERT @created VALUES ('FK_InvitationRegistrations_Invitations', 'created');
    END
    ELSE INSERT @created VALUES ('FK_InvitationRegistrations_Invitations', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InvitationRegistrations_Outcomes')
    BEGIN
        ALTER TABLE invites.InvitationRegistrations WITH CHECK
            ADD CONSTRAINT FK_InvitationRegistrations_Outcomes
            FOREIGN KEY (InvitationOutcomeId) REFERENCES invites.InvitationOutcomes (InvitationOutcomeId);
        INSERT @created VALUES ('FK_InvitationRegistrations_Outcomes', 'created');
    END
    ELSE INSERT @created VALUES ('FK_InvitationRegistrations_Outcomes', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InvitationRegistrations_SourceRegistration')
    BEGIN
        ALTER TABLE invites.InvitationRegistrations WITH CHECK
            ADD CONSTRAINT FK_InvitationRegistrations_SourceRegistration
            FOREIGN KEY (SourceRegistrationId) REFERENCES Jobs.Registrations (RegistrationID);
        INSERT @created VALUES ('FK_InvitationRegistrations_SourceRegistration', 'created');
    END
    ELSE INSERT @created VALUES ('FK_InvitationRegistrations_SourceRegistration', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InvitationRegistrations_LebUser')
    BEGIN
        ALTER TABLE invites.InvitationRegistrations WITH CHECK
            ADD CONSTRAINT FK_InvitationRegistrations_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers (Id);
        INSERT @created VALUES ('FK_InvitationRegistrations_LebUser', 'created');
    END
    ELSE INSERT @created VALUES ('FK_InvitationRegistrations_LebUser', 'already there');

    ----------------------------------------------------------------------------------------------
    -- 6. Indexes.
    --    Every FK column gets one: without it, deleting a parent row scans the child table.
    --    IX_Invitations_TargetJobId also serves the search filter, which always knows the target.
    ----------------------------------------------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Invitations_TargetJobId' AND object_id=OBJECT_ID('invites.Invitations'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_Invitations_TargetJobId ON invites.Invitations (TargetJobId) INCLUDE (InviteKindId, ExpiresAt);
        INSERT @created VALUES ('IX_Invitations_TargetJobId', 'created');
    END
    ELSE INSERT @created VALUES ('IX_Invitations_TargetJobId', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Invitations_SourceJobId' AND object_id=OBJECT_ID('invites.Invitations'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_Invitations_SourceJobId ON invites.Invitations (SourceJobId);
        INSERT @created VALUES ('IX_Invitations_SourceJobId', 'created');
    END
    ELSE INSERT @created VALUES ('IX_Invitations_SourceJobId', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Invitations_InviteKindId' AND object_id=OBJECT_ID('invites.Invitations'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_Invitations_InviteKindId ON invites.Invitations (InviteKindId);
        INSERT @created VALUES ('IX_Invitations_InviteKindId', 'created');
    END
    ELSE INSERT @created VALUES ('IX_Invitations_InviteKindId', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Invitations_LebUserId' AND object_id=OBJECT_ID('invites.Invitations'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_Invitations_LebUserId ON invites.Invitations (LebUserId);
        INSERT @created VALUES ('IX_Invitations_LebUserId', 'created');
    END
    ELSE INSERT @created VALUES ('IX_Invitations_LebUserId', 'already there');

    -- InvitationId is the SECOND clustered key column, so it needs its own index for the FK check
    -- and for "all rows of this send".
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_InvitationRegistrations_InvitationId' AND object_id=OBJECT_ID('invites.InvitationRegistrations'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_InvitationRegistrations_InvitationId
            ON invites.InvitationRegistrations (InvitationId) INCLUDE (InvitationOutcomeId);
        INSERT @created VALUES ('IX_InvitationRegistrations_InvitationId', 'created');
    END
    ELSE INSERT @created VALUES ('IX_InvitationRegistrations_InvitationId', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_InvitationRegistrations_OutcomeId' AND object_id=OBJECT_ID('invites.InvitationRegistrations'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_InvitationRegistrations_OutcomeId ON invites.InvitationRegistrations (InvitationOutcomeId);
        INSERT @created VALUES ('IX_InvitationRegistrations_OutcomeId', 'created');
    END
    ELSE INSERT @created VALUES ('IX_InvitationRegistrations_OutcomeId', 'already there');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_InvitationRegistrations_LebUserId' AND object_id=OBJECT_ID('invites.InvitationRegistrations'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_InvitationRegistrations_LebUserId ON invites.InvitationRegistrations (LebUserId);
        INSERT @created VALUES ('IX_InvitationRegistrations_LebUserId', 'created');
    END
    ELSE INSERT @created VALUES ('IX_InvitationRegistrations_LebUserId', 'already there');

    ----------------------------------------------------------------------------------------------
    -- 7. Seed the lookups. Insert-if-missing by id; an edited name is left alone.
    ----------------------------------------------------------------------------------------------
    ;WITH k (Id, Name) AS (
        SELECT 1, N'Player registration'   UNION ALL
        SELECT 2, N'Club Rep registration' UNION ALL
        SELECT 3, N'Schedule preview'
    )
    INSERT invites.InviteKinds (InviteKindId, InviteKindName)
    SELECT k.Id, k.Name FROM k
    WHERE NOT EXISTS (SELECT 1 FROM invites.InviteKinds x WHERE x.InviteKindId = k.Id);
    INSERT @created VALUES ('seed InviteKinds', CAST(@@ROWCOUNT AS nvarchar(10)) + ' row(s)');

    ;WITH o (Id, Name) AS (
        SELECT 1, N'Sent'      UNION ALL
        SELECT 2, N'Failed'    UNION ALL
        SELECT 3, N'Opted out'
    )
    INSERT invites.InvitationOutcomes (InvitationOutcomeId, InvitationOutcomeName)
    SELECT o.Id, o.Name FROM o
    WHERE NOT EXISTS (SELECT 1 FROM invites.InvitationOutcomes x WHERE x.InvitationOutcomeId = o.Id);
    INSERT @created VALUES ('seed InvitationOutcomes', CAST(@@ROWCOUNT AS nvarchar(10)) + ' row(s)');

    -- "In progress" deliberately absent: existence is success, on both sides.
    ;WITH s (Id, Name) AS (
        SELECT 1, N'Accepted'       UNION ALL
        SELECT 2, N'Sent'           UNION ALL
        SELECT 3, N'Failed to send' UNION ALL
        SELECT 4, N'Opted out'      UNION ALL
        SELECT 5, N'Expired'        UNION ALL
        SELECT 6, N'Offered'        UNION ALL
        SELECT 7, N'Not invited'
    )
    INSERT invites.InviteStatuses (InviteStatusId, InviteStatusName)
    SELECT s.Id, s.Name FROM s
    WHERE NOT EXISTS (SELECT 1 FROM invites.InviteStatuses x WHERE x.InviteStatusId = s.Id);
    INSERT @created VALUES ('seed InviteStatuses', CAST(@@ROWCOUNT AS nvarchar(10)) + ' row(s)');

    ----------------------------------------------------------------------------------------------
    -- 8. Report
    ----------------------------------------------------------------------------------------------
    SELECT Step, Action FROM @created;

    SELECT 'invites.InviteKinds'          AS TableName, COUNT(*) AS Rows FROM invites.InviteKinds
    UNION ALL SELECT 'invites.InvitationOutcomes',      COUNT(*) FROM invites.InvitationOutcomes
    UNION ALL SELECT 'invites.InviteStatuses',          COUNT(*) FROM invites.InviteStatuses
    UNION ALL SELECT 'invites.Invitations',             COUNT(*) FROM invites.Invitations
    UNION ALL SELECT 'invites.InvitationRegistrations', COUNT(*) FROM invites.InvitationRegistrations;

    IF @Commit = 1
    BEGIN
        COMMIT TRANSACTION;
        PRINT 'COMMITTED.';
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT 'PREVIEW ONLY — rolled back. Set @Commit = 1 to apply.';
    END
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT 'FAILED — nothing applied.';
    THROW;
END CATCH;

/*  ROLLBACK, if this is ever abandoned. Deliberately commented out; never run by accident.
    Drops only objects this script created — no existing object is touched.

    DROP TABLE IF EXISTS invites.InvitationRegistrations;
    DROP TABLE IF EXISTS invites.Invitations;
    DROP TABLE IF EXISTS invites.InviteStatuses;
    DROP TABLE IF EXISTS invites.InvitationOutcomes;
    DROP TABLE IF EXISTS invites.InviteKinds;
    DROP SCHEMA IF EXISTS invites;
*/
