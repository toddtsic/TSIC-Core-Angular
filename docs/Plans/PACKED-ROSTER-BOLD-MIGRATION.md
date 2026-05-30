# Packed Roster PDF Migration — Bold Reports (Pattern A)

Migrating the Tournament Roster Packed family of Crystal Reports PDFs to in-process Bold Reports RDLs. This doc captures the working layout pattern + status so the work can be resumed in a fresh session.

## Status

| Report | Layout | Status | Commits |
|---|---|---|---|
| `TournamentRosterPacked.rdl` | 3-up newspaper grid | Done | `bc9b91c1`, `e4142985` |
| `TournamentRosterPacked_CollegeCommit.rdl` | 2-up + college name wrap | Done | `19ff3fd5`, `e910b026` |
| _more variants (TBD)_ | — | Pending | — |

Both reports render via `BoldReports.Net.Core` in `Services/Reporting/ReportingService.cs`. The RDLs live at `TSIC-Core-Angular/src/backend/TSIC.API/Reports/`. When running in the VSCode debugger, `ContentRootPath` resolves to the source folder, so RDL edits are picked up on the next request — no rebuild, no copy step.

## Pattern A (full-width title + N-up team-card grid)

**Why not Page.Columns?** SSRS `<Columns>N</Columns>` is body-wide; anything in body — including the agDiv title — flows through every column, repeating the title N times. Pattern A puts the title in a static row of an outer per-agDiv Tablix and the team cards in a nested `gridTablix` doing Mod / Ceiling math. Title prints full-width once per page; cards flow Left → Right then Top → Bottom newspaper-style.

### Outer matrixTablix (per agDiv)

```
<Tablix Name="matrixTablix">  Width = 554pt
  TablixBody
    TablixColumn  554pt
    TablixRow #1  22pt   →  txtDivisionTitle (=Fields!agDiv.Value, 12pt Bold, centered)
    TablixRow #2  640pt  →  <Tablix Name="gridTablix">  (the N-up grid, see below)
  TablixColumnHierarchy
    <TablixMember />     (single static)
  TablixRowHierarchy
    Group DivisionGroup expr =Fields!agDiv.Value
      PageBreak BreakLocation = Between
      (NO ResetPageNumber — keeps report-wide "X of Y" page numbering)
    Inner TablixMembers
      [KeepWithGroup=After + RepeatOnNewPage=true,  <TablixMember />]
```

### Inner gridTablix (N-up team cards)

```
<Tablix Name="gridTablix">  Width = 554pt
  TablixBody
    TablixColumn  (pageWidth / N → 184pt for 3-up, 277pt for 2-up)
    TablixRow 320pt → cardOuter Rectangle (column − 6pt for 3pt margins)
  TablixColumnHierarchy
    Group GridCol expr =(Fields!divTeamRow.Value - 1) Mod N
  TablixRowHierarchy
    Group GridRow expr =Math.Ceiling(CDbl(Fields!divTeamRow.Value) / N)
```

### Card sizing math

| Variant | N | gridTablix col | cardOuter | innerRoster width |
|---|---|---|---|---|
| 3-up base Packed | 3 | 184pt | 178pt (Left=3) | 172pt (Left=4, inset 4+2) |
| 2-up CC Packed | 2 | 277pt | 271pt (Left=3) | 265pt (Left=4, inset 4+2) |

- pageWidth = 554pt (8.5" minus 0.4" margins each side)
- cardOuter = column − 6pt (3pt L/R margin within the gridTablix cell)
- innerRoster inset within cardOuter prevents row-divider lines from extending past the card border
- innerRoster Top=28pt clears the 26pt titleUnderline + 2pt gap

### Card chrome (inside cardOuter)

```
txtTeamHeader     Top=0    Height=14   =First(Fields!clubTeamName.Value)   8pt Bold
txtTeamRepLine    Top=14   Height=12   rep name + email + cellphone        6.5pt
titleUnderline    Top=26   Line, 0.75pt black, full card width
innerRoster       Top=28   Height=282  (per-variant player columns)
```

### innerRoster (player rows)

- `DataSetName = MainReportData` — Bold automatically inherits the parent group scope when a Tablix is nested inside another Tablix's CellContents
- `TablixRowHierarchy` has one `innerDetail` group with composite-key dedup:
  ```
  =Fields!teamID.Value.ToString() + "|" + Fields!player.Value +
   "|" + CStr(Fields!roleSort.Value) +
   "|" + IIf(IsNothing(Fields!uniform_no.Value), "", Fields!uniform_no.Value)
  ```
- Each cell BottomBorder: `Color=#888888`, `Width=0.5pt`, `Style=IIf(Fields!isLastRow.Value = 1, "None", "Solid")` so the last row has no underline
- To wrap a column instead of truncating: set `CanGrow=true` on the Textbox and drop the `Left(…, N)` wrapper from the value expression (this is how CC's collegeCommit was unlocked in `e910b026`)

### Per-variant column sets

| Variant | Columns | Total |
|---|---|---|
| 3-up base | uniform 20 / name 80 / position 30 / school 42 (`Left(…,14)`) | 172pt |
| 2-up CC   | uniform 22 / name 100 / position 38 / gradYear 26 / gpa 22 / collegeCommit 57 (wraps) | 265pt |

## Backend wiring

- `Services/Reporting/ReportingService.cs` line ~286: `var rdlPath = Path.Combine(_hostEnvironment.ContentRootPath, "Reports", $"{safeName}.rdl");` — opens the RDL fresh on each request (no in-process cache), so edits go live immediately under the debugger
- Each report has its own `reporting_migrate.<ReportName>_Flat` stored procedure returning the 20-column flat dataset (`agegroupName, divName, agDiv, teamID, clubTeamName, clubRepName, clubRepCellphone, clubRepEmail, divTeamRow, player, uniform_no, position, school_name, bCollegeCommit, gradYear, gpa, collegeCommit, roleName, roleSort, isLastRow`)
- Per-report migrate-proc convention: every migrated report gets its OWN proc even when sharing a base source proc (keeps the spName-keyed dispatch in PDF/Excel services a safe 1:1 key)

## Authoritative design spec

The legacy Crystal Reports PDF outputs are the design spec. Match them; don't guess. Recent reference downloads are typically in `C:/Users/Administrator/Downloads/TSIC-Export*.pdf`.

## Traps already burned (don't repeat)

- **`<Columns>N</Columns>` in `<Page>`** activates body-wide columns → title repeats per column. Remove it for Pattern A.
- **`<ResetPageNumber>true</ResetPageNumber>` in PageBreak** → "1 of 1" on every page. Remove for report-wide page numbering.
- **Row-divider lines extending past the card border** → innerRoster must be inset (not flush with cardOuter Left=0 / Width=cardWidth).
- **Building a minimal RDL from scratch to debug Bold `#Error` PDFs** → don't. Copy a known-working RDL (this family's base) and modify in place. Bold's error reporting is too thin for differential debugging from a stub.

## Resuming in a new session

Paste this into a fresh Claude Code window:

> Resuming Packed Roster PDF migration via Bold Reports. Read `docs/Plans/PACKED-ROSTER-BOLD-MIGRATION.md` for the working Pattern A layout + current status. Next variant: \<name the RDL\>.
