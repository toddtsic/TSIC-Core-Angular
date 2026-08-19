/**
 * Column definitions for the LADT sibling comparison grids.
 * Each level (league, agegroup, division, team) defines its own columns
 * that map DTO field names to human-readable headers with display metadata.
 */

export interface LadtColumnDef {
  field: string;
  header: string;
  type: 'string' | 'number' | 'boolean' | 'currency' | 'date' | 'dateOnly' | 'fees' | 'modifier' | 'phase' | 'identity';
  group?: string;
  frozen?: boolean;
  width?: string;
  /** When set, renders a color swatch dot using the value from this field on the row */
  colorField?: string;
  /**
   * `type: 'identity'` only — the value stacked UNDER `field` in a single cell. A team is
   * identified by club AND team name together, which is 320px as two columns and does not
   * fit a phone; stacked it is ~210px. Mirrors how the tree already renders a team node
   * (.tree-label-group in ladt.component.html), so the grid reads the same as the tree the
   * director just came from.
   */
  secondaryField?: string;
  /**
   * When set, the header renders an ⓘ info-tooltip beside the label (AM-038:
   * "3rd Party" meant nothing to Ann without a gloss). NOTE: tooltip columns
   * render via a dedicated e-column branch in ladt-sibling-grid that currently
   * supports boolean + plain-text cells only — extend that branch's @switch
   * before putting a tooltip on a fees/modifier/phase column.
   */
  headerTooltip?: string;
  /**
   * Let the cell WRAP to as many lines as the value needs instead of ellipsizing at the
   * column width. For names the director is scanning for (club, team), a truncated value
   * is unreadable without hovering the row; the row simply gets taller instead. Applies to
   * the body cell only — headers keep their own tuned wrap rules in ladt-sibling-grid.
   */
  wrap?: boolean;
}

// ── League ──

// AM-038 nit 2 (Ann): league grid fits WITHOUT horizontal scroll — league fee cells
// only say "see age group level", so Fees/EBD/Late Fee shrink and the frozen name
// column grows to show full league names untruncated.
export const LEAGUE_COLUMNS: LadtColumnDef[] = [
  { field: 'leagueName', header: 'League', type: 'string', frozen: true, width: '240px' },
  { field: 'sportName', header: 'Sport', type: 'string', width: '110px' },
  { field: '_fees', header: 'Fees', type: 'fees', width: '140px' },
  { field: '_earlyBird', header: 'Early Bird', type: 'modifier', width: '90px' },
  { field: '_lateFee', header: 'Late Fee', type: 'modifier', width: '90px' },
  { field: '_phase', header: 'Payment Phase', type: 'phase', width: '180px' },
  { field: 'rescheduleEmailsToAddon', header: 'Reschedule Emails', type: 'string', width: '180px' },
  // 08-02 (Todd): 95px left a 57px text box (width − 38px chrome), which clipped
  // the wrapped second line to "CONTA…" / "STANDI…". Uppercase "STANDINGS" needs
  // ~72px at 12px/600 — 115px fits both with headroom.
  { field: 'bHideContacts', header: 'Hide Contacts', type: 'boolean', width: '115px' },
  { field: 'bHideStandings', header: 'Hide Standings', type: 'boolean', width: '115px' },
];

// ── Agegroup ──

// AM-038 nit 4 (Ann): every width fits its longest header WORD — paired with the
// header CSS in ladt-sibling-grid, headers wrap only at spaces, never
// "GE NDER" / "CHA MPS BY DIV".
// AM-038 re-open (Ann, 07-31 — Todd go 08-01): the rarely-used Settings columns
// (Self Roster / Champs by Div / Hide Standings) are DROPPED from the grid so
// Age Group fits with no horizontal scrollbar. The grid is display-only — all
// remain editable in the age-group fly-in, which is the only edit surface
// anyway. Do not re-add without killing the scrollbar some other way.
// 08-01 (Todd): bAllowApiRosterAccess returned as the narrow "3rd Party"
// boolean — needed at a glance; Gender header shrunk to M/F (values are
// single letters) to pay for part of it.
export const AGEGROUP_COLUMNS: LadtColumnDef[] = [
  { field: 'agegroupName', header: 'Age Group', type: 'string', frozen: true, width: '180px', colorField: 'color' },
  // AM-038 re-open (08-02): header text gets width − 38px of chrome (16px cell
  // padding + 22px sort-icon reserve) before ej2's .e-headertext ellipsizes —
  // 60px left 22px for the unbreakable "M/F" (~24px) → "M…". 70px fits it.
  { field: 'gender', header: 'M/F', type: 'string', width: '70px' },
  { field: '_fees', header: 'Fees', type: 'fees', width: '220px' },
  { field: '_earlyBird', header: 'Early Bird', type: 'modifier', width: '120px' },
  { field: '_lateFee', header: 'Late Fee', type: 'modifier', width: '120px' },
  { field: '_phase', header: 'Payment Phase', type: 'phase', width: '180px' },
  // Limits
  { field: 'maxTeams', header: 'Max Teams', type: 'number', group: 'Limits', width: '95px' },
  // Roster — AR-006 (Ann): the age group is where self-rostering is actually decided.
  // Across 51,411 teams, 44,249 are self-rosterable via their AGE GROUP's flag and exactly
  // ONE is enabled by its own team-level checkbox alone, so this column is the answer to
  // "can players self-roster here?" in all but a single row. 85px matches the identical
  // column already on the Team grid — same field, header, and renderer, so the two grids
  // agree on sight. The flags are OR'd (TeamSelfRosterAvailability), NOT a cascade, which
  // is what the ⓘ has to say: neither level overrides the other.
  {
    field: 'bAllowSelfRostering', header: 'Self Roster', type: 'boolean', group: 'Roster', width: '85px',
    headerTooltip: 'Players may add themselves to teams in this age group. An individual team can also switch this on by itself — either one is enough, neither overrides the other. Teams still only accept self-rostering while inside their own registration window.',
  },
  // AM-038 re-open (Ann, 08-01): header needs the ⓘ gloss — "3rd Party" alone
  // meant nothing. 100px = "PARTY" + ⓘ on the wrapped line + the 38px chrome
  // (70px ellipsized at "3RD PA…"). Copy (Todd, 08-02): the flag opts the age
  // group into the Third-Party Roster Export library report — NOT live API
  // access (the free SportsRecruits feed is retired; clients hand data to third
  // parties themselves, at their own risk). Field name keeps the legacy "Api".
  {
    field: 'bAllowApiRosterAccess', header: '3rd Party', type: 'boolean', width: '100px',
    headerTooltip: 'Age groups with this enabled appear in the Third-Party Roster Export (Report Library) — you run the export and share it at your discretion.',
  },
];

// ── Division ──

export const DIVISION_COLUMNS: LadtColumnDef[] = [
  { field: 'divName', header: 'Division', type: 'string', frozen: true, width: '180px' },
  { field: 'maxRoundNumberToShow', header: 'Max Round#', type: 'number', width: '75px' },
];

// ── Team ──

export const TEAM_COLUMNS: LadtColumnDef[] = [
  // Club and team names run long and 160px each is the most the frozen region can spend
  // before the scrollable columns lose the viewport — so they WRAP rather than ellipsize.
  { field: 'clubName', header: 'Club', type: 'string', frozen: true, width: '160px', wrap: true },
  { field: 'teamName', header: 'Team', type: 'string', frozen: true, width: '160px', wrap: true },
  // AM-038 nit 3 (Ann): "ACTI VE" / "PLAYE RS" clipped mid-word — widths fit the header word
  { field: 'active', header: 'Active', type: 'boolean', width: '85px' },
  // AM-038 re-open (Ann, 08-01): 90px ellipsized the single-word header ("PLAYE…")
  { field: 'playerCount', header: 'Players', type: 'number', width: '100px' },
  { field: 'maxCount', header: 'Max Roster', type: 'number', width: '95px' },
  // Dates come BEFORE the fee columns (AM-038): with them trailing ~710px of
  // Fees/EBD/LateFee/Phase, Start/End/Effective/Expires were always off-screen.
  { field: 'startdate', header: 'Start', type: 'date', group: 'Dates', width: '100px' },
  { field: 'enddate', header: 'End', type: 'date', group: 'Dates', width: '100px' },
  { field: 'effectiveasofdate', header: 'Effective', type: 'date', group: 'Dates', width: '100px' },
  { field: 'expireondate', header: 'Expires', type: 'date', group: 'Dates', width: '100px' },
  { field: '_fees', header: 'Fees', type: 'fees', width: '220px' },
  { field: '_earlyBird', header: 'Early Bird', type: 'modifier', width: '120px' },
  { field: '_lateFee', header: 'Late Fee', type: 'modifier', width: '120px' },
  { field: '_phase', header: 'Payment Phase', type: 'phase', width: '180px' },
  { field: 'divRank', header: 'Rank', type: 'number', width: '75px' },
  { field: 'divisionRequested', header: 'Div Requested', type: 'string', width: '140px' },
  { field: 'lastLeagueRecord', header: 'Last Record', type: 'string', width: '90px' },
  { field: 'levelOfPlay', header: 'LOP', type: 'string', width: '90px' },
  // Roster — no 'bHideRoster' column by design: it was never a director setting (legacy exposed no UI
  // for it and its stored values are noise). Roster visibility is the event-level "Allow RosterView"
  // toggles, plus a server-side hide for WAITLIST/Dropped/Registration holding agegroups. See CR-095.
  { field: 'bAllowSelfRostering', header: 'Self Roster', type: 'boolean', group: 'Roster', width: '85px' },
  // Eligibility
  { field: 'gender', header: 'Gender', type: 'string', group: 'Eligibility', width: '80px' },
  // Advanced
  { field: 'requests', header: 'Requests', type: 'string', group: 'Advanced', width: '140px' },
  { field: 'teamComments', header: 'Comments', type: 'string', group: 'Advanced', width: '140px' },
];

/** Maps hierarchy level (0-3) to its column definitions */
export const COLUMNS_BY_LEVEL: LadtColumnDef[][] = [
  LEAGUE_COLUMNS,
  AGEGROUP_COLUMNS,
  DIVISION_COLUMNS,
  TEAM_COLUMNS,
];

// ── Mobile ──
//
// Below 768px the sibling grid is an IDENTIFY-AND-TAP surface, not a comparison
// surface: the director finds the right row and taps it to open the fly-in, which
// is where every edit happens anyway. So each level keeps only what distinguishes
// one row from its siblings.
//
// Sized against a 390px viewport (iPhone 12/13/14/15 and the narrowest phone worth
// supporting). The 64px action column is always frozen, so a level's data columns
// must total under ~326px or the grid pans horizontally.
//
// The team level is the reason this exists: club + team as two frozen columns is
// 64 + 160 + 160 = 384px on a 390px screen, leaving ~6px of scrollable area — the
// grid was unusable. Stacked as one `identity` cell it is 274px total.
//
// Nothing here is `frozen`: with no horizontal overflow there is nothing to freeze
// against, and the frozen region is pure cost.

const LEAGUE_COLUMNS_MOBILE: LadtColumnDef[] = [
  { field: 'leagueName', header: 'League', type: 'string', width: '210px' },
  { field: 'sportName', header: 'Sport', type: 'string', width: '110px' },
];

const AGEGROUP_COLUMNS_MOBILE: LadtColumnDef[] = [
  { field: 'agegroupName', header: 'Age Group', type: 'string', width: '190px', colorField: 'color' },
  { field: 'gender', header: 'M/F', type: 'string', width: '70px' },
];

const DIVISION_COLUMNS_MOBILE: LadtColumnDef[] = [
  { field: 'divName', header: 'Division', type: 'string', width: '190px' },
  { field: 'maxRoundNumberToShow', header: 'Max Round#', type: 'number', width: '75px' },
];

const TEAM_COLUMNS_MOBILE: LadtColumnDef[] = [
  // Club is PRIMARY and team SECONDARY, matching the tree's team node — the
  // director is two taps from having seen exactly that pairing.
  { field: 'clubName', header: 'Team', type: 'identity', secondaryField: 'teamName', width: '210px', wrap: true },
  { field: 'playerCount', header: 'Players', type: 'number', width: '100px' },
];

/**
 * Maps hierarchy level (0-3) to its mobile column definitions. Applied by
 * `loadSiblings()` when the viewport is under 768px; desktop never reads this.
 */
export const MOBILE_COLUMNS_BY_LEVEL: LadtColumnDef[][] = [
  LEAGUE_COLUMNS_MOBILE,
  AGEGROUP_COLUMNS_MOBILE,
  DIVISION_COLUMNS_MOBILE,
  TEAM_COLUMNS_MOBILE,
];

/** Maps hierarchy level (0-3) to the DTO's primary key field */
export const ID_FIELD_BY_LEVEL = ['leagueId', 'agegroupId', 'divId', 'teamId'] as const;

/** Maps hierarchy level (0-3) to the frozen column's field name */
export const NAME_FIELD_BY_LEVEL = ['leagueName', 'agegroupName', 'divName', 'teamName'] as const;

/** Returns the total frozen column count (frozen data cols + 1 for the action column) */
export function countFrozenColumns(defs: LadtColumnDef[]): number {
  return 1 + defs.filter(c => c.frozen).length;
}
