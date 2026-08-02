/**
 * Column definitions for the LADT sibling comparison grids.
 * Each level (league, agegroup, division, team) defines its own columns
 * that map DTO field names to human-readable headers with display metadata.
 */

export interface LadtColumnDef {
  field: string;
  header: string;
  type: 'string' | 'number' | 'boolean' | 'currency' | 'date' | 'dateOnly' | 'fees' | 'modifier' | 'phase';
  group?: string;
  frozen?: boolean;
  width?: string;
  /** When set, renders a color swatch dot using the value from this field on the row */
  colorField?: string;
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
  { field: 'bHideContacts', header: 'Hide Contacts', type: 'boolean', width: '95px' },
  { field: 'bHideStandings', header: 'Hide Standings', type: 'boolean', width: '95px' },
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
  { field: 'gender', header: 'M/F', type: 'string', width: '60px' },
  { field: '_fees', header: 'Fees', type: 'fees', width: '220px' },
  { field: '_earlyBird', header: 'Early Bird', type: 'modifier', width: '120px' },
  { field: '_lateFee', header: 'Late Fee', type: 'modifier', width: '120px' },
  { field: '_phase', header: 'Payment Phase', type: 'phase', width: '180px' },
  // Limits
  { field: 'maxTeams', header: 'Max Teams', type: 'number', group: 'Limits', width: '95px' },
  { field: 'bAllowApiRosterAccess', header: '3rd Party', type: 'boolean', width: '70px' },
];

// ── Division ──

export const DIVISION_COLUMNS: LadtColumnDef[] = [
  { field: 'divName', header: 'Division', type: 'string', frozen: true, width: '180px' },
  { field: 'maxRoundNumberToShow', header: 'Max Round#', type: 'number', width: '75px' },
];

// ── Team ──

export const TEAM_COLUMNS: LadtColumnDef[] = [
  { field: 'clubName', header: 'Club', type: 'string', frozen: true, width: '160px' },
  { field: 'teamName', header: 'Team', type: 'string', frozen: true, width: '160px' },
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

/** Maps hierarchy level (0-3) to the DTO's primary key field */
export const ID_FIELD_BY_LEVEL = ['leagueId', 'agegroupId', 'divId', 'teamId'] as const;

/** Maps hierarchy level (0-3) to the frozen column's field name */
export const NAME_FIELD_BY_LEVEL = ['leagueName', 'agegroupName', 'divName', 'teamName'] as const;

/** Returns the total frozen column count (frozen data cols + 1 for the action column) */
export function countFrozenColumns(defs: LadtColumnDef[]): number {
  return 1 + defs.filter(c => c.frozen).length;
}
