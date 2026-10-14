# What will go wrong on Postgres without the build telling us

Read-only inventory of `master` at `87f6929e1`. No code was changed to produce it. Paths
are under `TSIC-Core-Angular/src/backend/`.

**The rule that sorts this list.** When the Postgres branch removes the SQL Server
packages, the compiler refuses to build and lists every line that still uses a SQL Server
piece. Those need no work on `master`; the compiler is the checklist. This document is
about what the compiler will *not* list.

The compiler catches a line only if the SQL Server packages are removed from **both**
projects: `Microsoft.EntityFrameworkCore.SqlServer` from `TSIC.Infrastructure` and
`TSIC.API`, and the direct `Microsoft.Data.SqlClient` reference from `TSIC.API`.

## 1. The compiler WILL catch these (no work on `master`)

Listed only so nobody wonders about them.

| Where | What it is |
|---|---|
| 20 calls in 5 repositories | `EF.Functions.DateDiffDay` / `DateDiffMonth` / `DateDiffHour` / `DateFromParts` |
| `TSIC.Infrastructure/Repositories/ScheduleRepository.cs:677-687` | The schedule slot lock (`sp_getapplock`) |
| `TSIC.Infrastructure/Repositories/TeamChatRepository.cs:228, 296` | The team chat thread lock (`sp_getapplock`) |
| `TSIC.Infrastructure/Repositories/AdnReconciliationRepository.cs:128-135` | The month-end Authorize.Net match, a SQL Server script on its own connection |
| `TSIC.Infrastructure/Repositories/CustomerJobRevenueRepository.cs:28-51` | The reader for the two old revenue stored procedures |
| `TSIC.Infrastructure/Repositories/ReportingRepository.cs:352-402` | The leftover "run this stored procedure by name" path |
| `TSIC.Infrastructure/Repositories/RegistrationRepository.cs:1155` | The retry that recognises a SQL Server deadlock by its error number |
| `TSIC.API/Services/Reporting/ReportingService.cs:212` | The "name already taken" check by SQL Server error number |
| `TSIC.API/Services/Usage/UsageWriterBackgroundService.cs:303-316` | The SQL Server bulk loader that writes usage records |
| `TSIC.API/Program.cs:559-580` | The three places the app is told to use SQL Server |

**Care needed at the move, even though the compiler finds them:** the two locks must be
tested with two requests fired at the same instant (a replacement that does not really
lock looks fine in ordinary testing), and the month-end match is real conversion work.

## 2. The compiler will NOT catch these

### 2a. Builds, then errors when that feature is used (LOUD, not quiet)

These fail with an error the first time the feature is used, and save nothing wrong. They
are found by using each feature once on Postgres. The quiet ones are 2b and 2c.

| Where | What goes wrong |
|---|---|
| `TSIC.Infrastructure/Repositories/TeamChatRepository.cs:49, 64-75` | Draws the next chat message number with a SQL Server command sent as plain text. Sending a chat message fails. |
| `TSIC.Infrastructure/Repositories/JobCloneRepository.cs:375` | Counts rows per table using SQL Server square-bracket names sent as plain text. Cloning a job fails. |
| `TSIC.Infrastructure/Repositories/JobRepository.cs:118`, `AdultRegistrationRepository.cs:56` | Job lookup by web path names a SQL Server text rule that does not exist on Postgres. This lookup is on the path of most requests. |
| `TSIC.API/Reports/TournamentRosterPacked.rdl`, `TournamentRosterPacked_CollegeCommit.rdl` | Report files that name a stored procedure. |
| `AppLog.TimeStamp`, `AspNetUsers.LockoutEnd`, `CalendarEvents.StartTime` / `EndTime` | The four date fields that carry a time zone. Postgres accepts only UTC when saving them. |

### 2b. Builds, runs, and quietly does the wrong thing

| Where | What goes wrong |
|---|---|
| `TSIC.Infrastructure/Repositories/FieldRepository.cs:74` | Hides numbered fields such as "1-Field A" using a pattern only SQL Server understands. On Postgres it would stop hiding them, with no error. |
| `TSIC.API/Services/NavEditorService.cs`, `TSIC.API/Services/Widgets/WidgetEditorService.cs` | Generate SQL Server scripts for download. They would keep producing scripts Postgres cannot run. |

### 2c. Differences in how Postgres behaves, across the whole app

Not places in the code. Settled by how the Postgres database is set up; each needs a
decision before the move rather than a code change now.

1. **Upper and lower case.** SQL Server treats "Smith" and "smith" as equal everywhere
   (logins, emails, team names, searches). Postgres does not unless told to.
2. **Trailing spaces.** SQL Server treats "ABC" and "ABC " as equal. Postgres does not.
   14 columns are fixed-width text, which is where padding lives.
3. **Sort order.** Blank values sort first on SQL Server and last on Postgres, and letters
   can sort differently. Lists and exports may come out in a different order.
4. **Lists with no stated order.** Where the code does not say how to sort, the two
   databases can return rows in different orders.
5. **The server clock.** About 590 places read the server's local time, and 90 columns
   stamp it automatically. The Postgres server must be set to Arizona time.
6. **Two people at once.** `RegistrationRepository.cs:1129` and `ScheduleRepository.cs:1841`
   rely on SQL Server making the second person wait. Postgres rejects one of them instead.
   It shows only under load.
7. **Four all-or-nothing saves** (`TeamRegistrationController.cs:1015`, `ClubService.cs:179`,
   `FamilyService.cs:429, 524`) use a mechanism Postgres supports only while everything
   inside uses a single database connection.

## What this list does not cover

- Queries that EF can translate for SQL Server but not for Postgres. They build, then fail
  when the screen is opened, and no search of the code can find them. The only way to find
  them is to open every screen and run every report on the Postgres copy.
- The search was by text. No place was found that reads one query while running another on
  the same connection (which Postgres does not allow), but a text search cannot prove there
  is none.
