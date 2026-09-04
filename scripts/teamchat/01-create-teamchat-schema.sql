/*
================================================================================
  TSIC-TEAMS  ::  Team Chat  ::  01 - Create teamchat schema
================================================================================
  Drafted by    : Claude (DDL text only -- not applied, not scaffolded)
  Applied by    : Todd, by hand
  Target        : TSICV5
  Drafted       : 2026-08-27      Revised: 2026-08-30
  Apply order   : dev (.\SS2016) first, then PHOENIX

  Prior draft kept as 01-create-teamchat-schema.sql.20260827-superseded.
  It is WRONG on time (UTC) and lacks the mute / quiet-hours storage the design
  contract requires. Do not apply it.

  WHAT THIS DOES
    Creates a NEW, isolated `teamchat` schema for the ground-up TSIC-TEAMS chat
    service: 1 sequence, 7 tables, and the indexes and foreign keys they need.

  WHAT THIS DOES NOT DO
    Nothing existing is altered, dropped, renamed or read. The legacy
    `chat.ChatMessages` table -- still written by the legacy Unify API -- is left
    completely untouched and keeps working exactly as it does today.

  >>> BLOCKING BEFORE APPLY: re-run the legacy row count on PHOENIX. <<<
      SELECT COUNT(*) AS Rows_Total, MIN(Created) AS Oldest, MAX(Created) AS Newest
      FROM chat.ChatMessages;
    Dev (2026-08-27) showed 134 rows, newest 2024-06-15 -- 14 months dead, which is
    what made isolating cheaper than altering. If PROD shows recent rows, the legacy
    writer is live and the cutover plan needs revisiting before this lands.

--------------------------------------------------------------------------------
  RULINGS BAKED INTO THIS SCRIPT -- do not "improve" these away
--------------------------------------------------------------------------------
  1. ONE THREAD PER TEAM. Everyone rostered sees every message. There is no
     conversation or participant abstraction, no direct messages, no subgroups,
     no coaches-only channel. Whole-team visibility is a SAFETY CONTROL on a
     surface that minors can publish to. Todd's ruling 2026-08-30. Never
     re-propose.

  2. ALL TIMES ARE ARIZONA LOCAL. Not UTC. AZ never changes clocks, so it acts as
     a stable reference the way GMT would; the front end does the conversion.
     SQL defaults use SYSDATETIME(); C# writes DateTime.Now. NEVER SYSUTCDATETIME()
     or DateTime.UtcNow -- that silently runs 7h ahead of every other column in
     this database. (The design artifact argues for UTC and calls the client's
     hardcoded -7 offset a defect. Under this rule that constant is the convention
     working as intended. The artifact is wrong on that point and the front-end
     hand-off must say so, or the conversion gets built twice.)

  3. PHOTOS ARE GATED LIKE MED FORMS. Files sit under the statics site but the
     folder is blocked from the public vhost by a <hiddenSegments> entry in the
     statics root web.config; the app streams them through an [Authorize]d
     controller off the filesystem, exactly as MedFormController does. Todd
     provisions the folder and the web.config by hand. The attachment's own
     AttachmentId IS the filename -- it is already a GUID, so it is already
     unguessable, and no separate token column is needed.

  4. BROAD ON PURPOSE. Several tables and columns below are inert on day one and
     are limited in code, not in schema -- so this schema is applied ONCE. Each
     is marked [Day 1] or [inert].

  AFTER APPLYING
    Re-scaffold EF entities by hand: scripts/3) RE-Scaffold-Db-Entities.ps1
    Do not hand-author entities or SqlDbContext mappings -- the scaffold owns
    those and will clobber hand edits.
================================================================================
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. Schema
------------------------------------------------------------------------------*/
IF SCHEMA_ID('teamchat') IS NULL
    EXEC ('CREATE SCHEMA teamchat');
GO

/*------------------------------------------------------------------------------
  2. Sequence -- hands out ordering numbers for the whole subsystem.

     A SEQUENCE, not IDENTITY: a delete, an edit, a pin and a reaction all draw a
     value from it too (see LastTouchSeq below), and an identity column can only
     be allocated by inserting a row. Global rather than per-team is fine -- gaps
     inside a team are irrelevant because every read is "> @cursor".

     NOT ENFORCEABLE IN DDL -- the write path MUST do this:
     NEXT VALUE FOR allocates OUTSIDE the transaction, so two concurrent posts can
     commit in the opposite order to their numbers. A reader that advances its
     cursor in between never sees the lower-numbered message again -- a silently
     vanished message. Take an exclusive app lock on the team and hold it across
     allocate + commit:

         EXEC sp_getapplock @Resource    = 'teamchat:<teamId>',
                            @LockMode    = 'Exclusive',
                            @LockOwner   = 'Transaction',
                            @LockTimeout = 5000;

     Only concurrent posts to the SAME team serialize; different teams never
     contend.
------------------------------------------------------------------------------*/
IF OBJECT_ID('teamchat.MessageSequence') IS NULL
    CREATE SEQUENCE teamchat.MessageSequence AS bigint START WITH 1 INCREMENT BY 1;
GO

/*------------------------------------------------------------------------------
  3. teamchat.Messages -- one row per chat message.  [Day 1]

     Seq            The message's PERMANENT position in the team thread. Immutable
                    once written. This is what scrollback pages through.

     LastTouchSeq   A NEW sequence value stamped on ANY change to this row --
                    insert, edit, delete, pin, reaction, moderation. THIS is the
                    catch-up cursor:

                        WHERE TeamId = @t AND LastTouchSeq > @since
                        ORDER BY LastTouchSeq

                    One index, one query shape, permanently. The alternative --
                    "Seq > @since OR DeletedSeq > @since" -- grows a new OR'd
                    column for every feature until it cannot be indexed at all.
                    On insert, LastTouchSeq = Seq (the same allocated value).

                    Without this, a phone that was asleep catches up on new
                    messages but silently misses edits, deletions, reactions and
                    pins that happened while it was off.

     RegId          The registration the message was sent FROM. Distinct from
                    CreatorUserId (the login) because a family account posts on
                    behalf of a child. Two different facts; cannot be backfilled.
                    NOTE for the read DTO: AuthorName resolves from CreatorUserId,
                    NOT from the player named on RegId -- otherwise an adult's
                    words appear under a minor's name and a coach cannot tell
                    whether they are talking to the parent or the kid.

     Kind           0 = member message, 1 = system message posted by the app
                    ("practice cancelled", "Jimmy added to roster"). Lets the
                    client render system rows distinctly without a second table.

     ClientMessageId  Phone-generated idempotency key. Makes a retry over a flaky
                    connection safe; UX_teamchat_Messages_Idem is the backstop for
                    the concurrent-retry race -- the service must catch its
                    violation and return the existing row, never throw a 500.

     Created        datetime2, ARIZONA local (ruling 2 above). Not UTC.

     DeletedSeq /   Soft delete. The row is NEVER removed: if something has to come
     DeletedByUserId  down, who said it is exactly what a club needs afterwards.
                    DeletedByUserId is a durable moderation record, deliberately
                    separate from lebUserID (which is row audit and is overwritten
                    by the next edit).

     EditedSeq /    Last edit. Prior text is retained in teamchat.MessageRevisions.
     EditedAt

     PinnedAt /     Pinned to the top of the thread. NULL = not pinned.
     PinnedByUserId

     modified /     House housekeeping columns -- row audit that nothing renders.
     lebUserID      See project_housekeeping_columns_convention.
------------------------------------------------------------------------------*/
IF OBJECT_ID('teamchat.Messages') IS NULL
BEGIN
    CREATE TABLE teamchat.Messages
    (
        MessageId        uniqueidentifier NOT NULL CONSTRAINT DF_teamchat_Messages_MessageId DEFAULT (newid()),
        TeamId           uniqueidentifier NOT NULL,
        JobId            uniqueidentifier NOT NULL,   -- denormalized from the team: saves a join on the cross-job gate. Kept deliberately.
        Seq              bigint           NOT NULL,
        LastTouchSeq     bigint           NOT NULL,
        Kind             tinyint          NOT NULL CONSTRAINT DF_teamchat_Messages_Kind    DEFAULT (0),
        Message          nvarchar(4000)   NOT NULL,   -- storage headroom; the write path enforces 2000 until told otherwise
        CreatorUserId    nvarchar(450)    NOT NULL,
        RegId            uniqueidentifier NOT NULL,
        ClientMessageId  uniqueidentifier NOT NULL,
        ReplyToMessageId uniqueidentifier NULL,
        Created          datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Messages_Created DEFAULT (sysdatetime()),

        EditedSeq        bigint           NULL,
        EditedAt         datetime2(7)     NULL,
        DeletedSeq       bigint           NULL,
        DeletedByUserId  nvarchar(450)    NULL,
        PinnedAt         datetime2(7)     NULL,
        PinnedByUserId   nvarchar(450)    NULL,

        modified         datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Messages_modified DEFAULT (sysdatetime()),
        lebUserID        nvarchar(450)    NULL,

        CONSTRAINT PK_teamchat_Messages PRIMARY KEY NONCLUSTERED (MessageId),

        CONSTRAINT FK_teamchat_Messages_Team
            FOREIGN KEY (TeamId)           REFERENCES Leagues.teams      (teamID),
        CONSTRAINT FK_teamchat_Messages_Job
            FOREIGN KEY (JobId)            REFERENCES Jobs.Jobs          (jobID),
        CONSTRAINT FK_teamchat_Messages_Creator
            FOREIGN KEY (CreatorUserId)    REFERENCES dbo.AspNetUsers    (Id),
        CONSTRAINT FK_teamchat_Messages_Reg
            FOREIGN KEY (RegId)            REFERENCES Jobs.Registrations (RegistrationID),
        CONSTRAINT FK_teamchat_Messages_ReplyTo
            FOREIGN KEY (ReplyToMessageId) REFERENCES teamchat.Messages  (MessageId),
        CONSTRAINT FK_teamchat_Messages_DeletedBy
            FOREIGN KEY (DeletedByUserId)  REFERENCES dbo.AspNetUsers    (Id),
        CONSTRAINT FK_teamchat_Messages_PinnedBy
            FOREIGN KEY (PinnedByUserId)   REFERENCES dbo.AspNetUsers    (Id),
        CONSTRAINT FK_teamchat_Messages_leb
            FOREIGN KEY (lebUserID)        REFERENCES dbo.AspNetUsers    (Id)
    );
END
GO

/*  UNIQUE CLUSTERED on (TeamId, Seq).

    Unique because the sequence already guarantees it, and declaring it drops the
    hidden 4-byte uniquifier off every row. Clustered here rather than on the PK
    because clustering a newid() GUID fragments the table on every insert, and
    because every scrollback read is "this team, below this number".

    A standalone UNIQUE on Seq alone was in the prior draft and is deliberately
    NOT here: a global unique index on a monotonically increasing key puts every
    team's inserts on one hot tail page, for no guarantee this does not already
    provide.                                                                    */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_teamchat_Messages_Team_Seq'
                 AND object_id = OBJECT_ID('teamchat.Messages'))
    CREATE UNIQUE CLUSTERED INDEX UX_teamchat_Messages_Team_Seq
        ON teamchat.Messages (TeamId, Seq);
GO

/*  The catch-up cursor. Every "what changed since I last looked" read uses this. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_teamchat_Messages_Team_LastTouch'
                 AND object_id = OBJECT_ID('teamchat.Messages'))
    CREATE NONCLUSTERED INDEX IX_teamchat_Messages_Team_LastTouch
        ON teamchat.Messages (TeamId, LastTouchSeq);
GO

/*  Idempotency backstop. The write path looks this key up first and returns the
    existing row on a hit; this index catches the concurrent retry.             */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_teamchat_Messages_Idem'
                 AND object_id = OBJECT_ID('teamchat.Messages'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_teamchat_Messages_Idem
        ON teamchat.Messages (TeamId, CreatorUserId, ClientMessageId);
GO

/*  Pinned messages -- a handful per team at most, so filtered.                 */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_teamchat_Messages_Team_Pinned'
                 AND object_id = OBJECT_ID('teamchat.Messages'))
    CREATE NONCLUSTERED INDEX IX_teamchat_Messages_Team_Pinned
        ON teamchat.Messages (TeamId, PinnedAt)
        WHERE PinnedAt IS NOT NULL;
GO

/*------------------------------------------------------------------------------
  4. teamchat.MemberTeamState -- per-registration state for one team.  [Day 1]

     Named MemberTeamState, not ReadState: it carries preferences as well as the
     read position, and the grain (RegId, TeamId) is exactly right for both.

     LastReadSeq   Drives the unread badge, which is DERIVED, never stored:

                       SELECT COUNT(*) FROM teamchat.Messages
                       WHERE TeamId = @t AND Seq > @lastReadSeq AND DeletedSeq IS NULL;

                   The upsert must take MAX(existing, incoming) -- never move the
                   cursor backwards, since pushes and fetches land out of order.

     Muted /       Required by the design contract, with an acceptance test: a muted
     MutedUntil    recipient still gets the row and still gets an incremented unread
                   count -- muting silences the doorbell, not the conversation.
                   Evaluated SERVER-SIDE during fan-out, because a backgrounded iOS
                   app runs no code and can filter nothing.

     QuietStart /  A nightly window with no pushes. ARIZONA times, per ruling 2 --
     QuietEndLocal no timezone lookup anywhere, no DST drift on our side. The front
                   end converts to and from whatever the user actually sees.
                   NOTE for the client: a user who observes DST sits at a different
                   AZ time in summer than in winter, so convert at save AND at
                   display -- do not store the number once and forget it.

     NotifyOnMention  Let a direct mention through even when muted.
     Pinned           Pin this team to the top of the member's chat list.
------------------------------------------------------------------------------*/
IF OBJECT_ID('teamchat.MemberTeamState') IS NULL
BEGIN
    CREATE TABLE teamchat.MemberTeamState
    (
        RegId           uniqueidentifier NOT NULL,
        TeamId          uniqueidentifier NOT NULL,
        LastReadSeq     bigint           NOT NULL CONSTRAINT DF_teamchat_MTS_LastReadSeq     DEFAULT (0),

        Muted           bit              NOT NULL CONSTRAINT DF_teamchat_MTS_Muted           DEFAULT (0),
        MutedUntil      datetime2(7)     NULL,
        QuietStartLocal time(0)          NULL,
        QuietEndLocal   time(0)          NULL,
        NotifyOnMention bit              NOT NULL CONSTRAINT DF_teamchat_MTS_NotifyOnMention DEFAULT (1),
        Pinned          bit              NOT NULL CONSTRAINT DF_teamchat_MTS_Pinned          DEFAULT (0),

        modified        datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_MTS_modified        DEFAULT (sysdatetime()),
        lebUserID       nvarchar(450)    NULL,

        CONSTRAINT PK_teamchat_MemberTeamState PRIMARY KEY CLUSTERED (RegId, TeamId),

        CONSTRAINT FK_teamchat_MTS_Reg
            FOREIGN KEY (RegId)     REFERENCES Jobs.Registrations (RegistrationID),
        CONSTRAINT FK_teamchat_MTS_Team
            FOREIGN KEY (TeamId)    REFERENCES Leagues.teams      (teamID),
        CONSTRAINT FK_teamchat_MTS_leb
            FOREIGN KEY (lebUserID) REFERENCES dbo.AspNetUsers    (Id)
    );
END
GO

/*------------------------------------------------------------------------------
  5. teamchat.Attachments -- photos on a message.  [inert until code enables]

     AttachmentId IS THE FILENAME. The file on disk is {AttachmentId}.jpg, with
     {AttachmentId}_t.jpg beside it for the thumbnail. It is already a GUID, so it
     is already unguessable -- no separate random token column -- and the path is
     derivable straight from the row, which makes orphan cleanup trivial: a file
     whose id has no row is an orphan.

     Files are NOT web-served. They live under the statics site but the folder is
     blocked from the public vhost by a <hiddenSegments> entry, and the app streams
     them through an [Authorize]d controller off the filesystem, exactly like
     MedFormController. TODD PROVISIONS THE FOLDER AND THE web.config BY HAND.
       Config key: FileStorage:TeamChatPhotosPath
         C:\Websites\TSIC-STATICS\TSIC-TEAMS-PHOTOS   (base + Development; Staging inherits)
         E:\Websites\TSIC-STATICS\TSIC-TEAMS-PHOTOS   (Production)
     The write path still calls Directory.CreateDirectory, as StoreImageService.cs
     does -- idempotent, and it covers the bucket subfolders. A <hiddenSegments>
     entry matches on URL SEGMENT, so every subfolder underneath is covered
     automatically; RegFileUploads\MedForms already proves this on that box.

     No StoredFileName and no Url column ON PURPOSE. The existing tables store
     absolute statics URLs, which is exactly why moving that site later would be a
     data migration rather than a file copy.

     Every upload is re-encoded to JPEG, dimension-capped, and has its EXIF
     STRIPPED -- a phone photo carries GPS, so a practice photo would publish the
     field and one taken at home would publish the house. Anything that does not
     decode as an image is rejected regardless of its extension. This mirrors
     StoreImageService, which is also the pattern to copy for the write path.

     OriginalFileName is display only ("beach-tournament.heic"); it never touches
     the disk path.

     STILL UNDECIDED -- do not invent answers: max dimensions, JPEG quality, max
     upload size, allowed input types, how many photos per message. Video was
     never discussed either way.
------------------------------------------------------------------------------*/
IF OBJECT_ID('teamchat.Attachments') IS NULL
BEGIN
    CREATE TABLE teamchat.Attachments
    (
        AttachmentId     uniqueidentifier NOT NULL CONSTRAINT DF_teamchat_Attachments_Id        DEFAULT (newid()),
        MessageId        uniqueidentifier NOT NULL,
        Kind             tinyint          NOT NULL CONSTRAINT DF_teamchat_Attachments_Kind      DEFAULT (0),  -- 0 = image
        OriginalFileName nvarchar(260)    NULL,
        ContentType      nvarchar(128)    NOT NULL,
        ByteSize         bigint           NOT NULL,
        Width            int              NULL,
        Height           int              NULL,
        SortOrder        int              NOT NULL CONSTRAINT DF_teamchat_Attachments_SortOrder DEFAULT (0),
        Created          datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Attachments_Created   DEFAULT (sysdatetime()),

        modified         datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Attachments_modified  DEFAULT (sysdatetime()),
        lebUserID        nvarchar(450)    NULL,

        CONSTRAINT PK_teamchat_Attachments PRIMARY KEY NONCLUSTERED (AttachmentId),

        CONSTRAINT FK_teamchat_Attachments_Message
            FOREIGN KEY (MessageId) REFERENCES teamchat.Messages (MessageId),
        CONSTRAINT FK_teamchat_Attachments_leb
            FOREIGN KEY (lebUserID) REFERENCES dbo.AspNetUsers   (Id)
    );

    CREATE CLUSTERED INDEX IX_teamchat_Attachments_Message
        ON teamchat.Attachments (MessageId, SortOrder);
END
GO

/*------------------------------------------------------------------------------
  6. teamchat.Reactions -- one row per (message, person, emoji).  [inert]

     Lets twenty parents acknowledge without twenty "ok" messages. Adding or
     removing one MUST bump the message's LastTouchSeq, or phones that already
     hold the message never learn about it.
------------------------------------------------------------------------------*/
IF OBJECT_ID('teamchat.Reactions') IS NULL
BEGIN
    CREATE TABLE teamchat.Reactions
    (
        MessageId     uniqueidentifier NOT NULL,
        CreatorUserId nvarchar(450)    NOT NULL,
        Emoji         nvarchar(16)     NOT NULL,
        RegId         uniqueidentifier NOT NULL,
        Created       datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Reactions_Created  DEFAULT (sysdatetime()),

        modified      datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Reactions_modified DEFAULT (sysdatetime()),
        lebUserID     nvarchar(450)    NULL,

        CONSTRAINT PK_teamchat_Reactions PRIMARY KEY CLUSTERED (MessageId, CreatorUserId, Emoji),

        CONSTRAINT FK_teamchat_Reactions_Message
            FOREIGN KEY (MessageId)     REFERENCES teamchat.Messages  (MessageId),
        CONSTRAINT FK_teamchat_Reactions_Creator
            FOREIGN KEY (CreatorUserId) REFERENCES dbo.AspNetUsers    (Id),
        CONSTRAINT FK_teamchat_Reactions_Reg
            FOREIGN KEY (RegId)         REFERENCES Jobs.Registrations (RegistrationID),
        CONSTRAINT FK_teamchat_Reactions_leb
            FOREIGN KEY (lebUserID)     REFERENCES dbo.AspNetUsers    (Id)
    );
END
GO

/*------------------------------------------------------------------------------
  7. teamchat.Mentions -- who was named in a message.  [inert]

     Mainly so a mention can pierce a mute (MemberTeamState.NotifyOnMention).
     Written by the server when the message is parsed; never trusted from the
     client.
------------------------------------------------------------------------------*/
IF OBJECT_ID('teamchat.Mentions') IS NULL
BEGIN
    CREATE TABLE teamchat.Mentions
    (
        MessageId uniqueidentifier NOT NULL,
        RegId     uniqueidentifier NOT NULL,

        modified  datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Mentions_modified DEFAULT (sysdatetime()),
        lebUserID nvarchar(450)    NULL,

        CONSTRAINT PK_teamchat_Mentions PRIMARY KEY CLUSTERED (MessageId, RegId),

        CONSTRAINT FK_teamchat_Mentions_Message
            FOREIGN KEY (MessageId) REFERENCES teamchat.Messages  (MessageId),
        CONSTRAINT FK_teamchat_Mentions_Reg
            FOREIGN KEY (RegId)     REFERENCES Jobs.Registrations (RegistrationID),
        CONSTRAINT FK_teamchat_Mentions_leb
            FOREIGN KEY (lebUserID) REFERENCES dbo.AspNetUsers    (Id)
    );
END
GO

/*------------------------------------------------------------------------------
  8. teamchat.MessageReports -- a member flags a message.  [inert]

     In the schema on day one even though nothing calls it yet. Chat hands a
     publishing surface to players and parents, including minors; the moment you
     need a way for someone to flag a message is the worst possible moment to be
     adding one.

     Reason: club-defined. Status: 0 = open, 1 = actioned, 2 = dismissed.
------------------------------------------------------------------------------*/
IF OBJECT_ID('teamchat.MessageReports') IS NULL
BEGIN
    CREATE TABLE teamchat.MessageReports
    (
        ReportId         uniqueidentifier NOT NULL CONSTRAINT DF_teamchat_Reports_Id       DEFAULT (newid()),
        MessageId        uniqueidentifier NOT NULL,
        ReporterUserId   nvarchar(450)    NOT NULL,
        ReporterRegId    uniqueidentifier NOT NULL,
        Reason           tinyint          NOT NULL CONSTRAINT DF_teamchat_Reports_Reason   DEFAULT (0),
        Note             nvarchar(1000)   NULL,
        Created          datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Reports_Created  DEFAULT (sysdatetime()),
        Status           tinyint          NOT NULL CONSTRAINT DF_teamchat_Reports_Status   DEFAULT (0),
        ResolvedByUserId nvarchar(450)    NULL,
        ResolvedAt       datetime2(7)     NULL,

        modified         datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Reports_modified DEFAULT (sysdatetime()),
        lebUserID        nvarchar(450)    NULL,

        CONSTRAINT PK_teamchat_MessageReports PRIMARY KEY NONCLUSTERED (ReportId),

        CONSTRAINT FK_teamchat_Reports_Message
            FOREIGN KEY (MessageId)        REFERENCES teamchat.Messages  (MessageId),
        CONSTRAINT FK_teamchat_Reports_Reporter
            FOREIGN KEY (ReporterUserId)   REFERENCES dbo.AspNetUsers    (Id),
        CONSTRAINT FK_teamchat_Reports_ReporterReg
            FOREIGN KEY (ReporterRegId)    REFERENCES Jobs.Registrations (RegistrationID),
        CONSTRAINT FK_teamchat_Reports_ResolvedBy
            FOREIGN KEY (ResolvedByUserId) REFERENCES dbo.AspNetUsers    (Id),
        CONSTRAINT FK_teamchat_Reports_leb
            FOREIGN KEY (lebUserID)        REFERENCES dbo.AspNetUsers    (Id)
    );

    CREATE CLUSTERED INDEX IX_teamchat_Reports_Status_Created
        ON teamchat.MessageReports (Status, Created);
END
GO

/*------------------------------------------------------------------------------
  9. teamchat.MessageRevisions -- the text as it was before an edit.  [inert]

     Same reasoning that makes deletes soft: on a surface minors publish to, what
     was said before someone tidied it is exactly what a club needs afterwards.
------------------------------------------------------------------------------*/
IF OBJECT_ID('teamchat.MessageRevisions') IS NULL
BEGIN
    CREATE TABLE teamchat.MessageRevisions
    (
        RevisionId     uniqueidentifier NOT NULL CONSTRAINT DF_teamchat_Revisions_Id         DEFAULT (newid()),
        MessageId      uniqueidentifier NOT NULL,
        PriorMessage   nvarchar(4000)   NOT NULL,
        ReplacedAt     datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Revisions_ReplacedAt DEFAULT (sysdatetime()),
        EditedByUserId nvarchar(450)    NOT NULL,

        modified       datetime2(7)     NOT NULL CONSTRAINT DF_teamchat_Revisions_modified   DEFAULT (sysdatetime()),
        lebUserID      nvarchar(450)    NULL,

        CONSTRAINT PK_teamchat_MessageRevisions PRIMARY KEY NONCLUSTERED (RevisionId),

        CONSTRAINT FK_teamchat_Revisions_Message
            FOREIGN KEY (MessageId)      REFERENCES teamchat.Messages (MessageId),
        CONSTRAINT FK_teamchat_Revisions_EditedBy
            FOREIGN KEY (EditedByUserId) REFERENCES dbo.AspNetUsers   (Id),
        CONSTRAINT FK_teamchat_Revisions_leb
            FOREIGN KEY (lebUserID)      REFERENCES dbo.AspNetUsers   (Id)
    );

    CREATE CLUSTERED INDEX IX_teamchat_Revisions_Message_ReplacedAt
        ON teamchat.MessageRevisions (MessageId, ReplacedAt);
END
GO

/*------------------------------------------------------------------------------
 10. Verification -- read-only, safe to re-run.
     Expect: 1 sequence, 7 tables, every FK NO_ACTION, and zero UTC defaults.
------------------------------------------------------------------------------*/
SELECT o.type_desc, SCHEMA_NAME(o.schema_id) + '.' + o.name AS ObjectName
FROM   sys.objects o
WHERE  SCHEMA_NAME(o.schema_id) = 'teamchat'
   AND o.type IN ('U', 'SO')
ORDER  BY o.type_desc, o.name;

SELECT t.name AS TableName, i.name AS IndexName,
       i.type_desc, i.is_unique, i.has_filter
FROM   sys.indexes i
JOIN   sys.tables  t ON t.object_id = i.object_id
WHERE  SCHEMA_NAME(t.schema_id) = 'teamchat'
   AND i.name IS NOT NULL
ORDER  BY t.name, i.name;

SELECT fk.name AS ForeignKey,
       OBJECT_NAME(fk.parent_object_id)           AS FromTable,
       SCHEMA_NAME(pt.schema_id) + '.' + pt.name  AS ReferencedTable,
       fk.delete_referential_action_desc          AS OnDelete
FROM   sys.foreign_keys fk
JOIN   sys.tables ct ON ct.object_id = fk.parent_object_id
JOIN   sys.tables pt ON pt.object_id = fk.referenced_object_id
WHERE  SCHEMA_NAME(ct.schema_id) = 'teamchat'
ORDER  BY FromTable, ForeignKey;

/*  Guard for ruling 2: no UTC defaults may exist in this schema. Expect ZERO rows. */
SELECT dc.name AS BadDefault, t.name AS TableName, dc.definition
FROM   sys.default_constraints dc
JOIN   sys.tables t ON t.object_id = dc.parent_object_id
WHERE  SCHEMA_NAME(t.schema_id) = 'teamchat'
   AND dc.definition LIKE '%utc%';
GO
