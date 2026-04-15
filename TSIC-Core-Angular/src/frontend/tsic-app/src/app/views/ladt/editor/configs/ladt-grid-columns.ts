/**
 * Column definitions for the LADT sibling comparison grids.
 * Each level (league, agegroup, division, team) defines its own columns
 * that map DTO field names to human-readable headers with display metadata.
 */

export interface LadtColumnDef {
  field: string;
  header: string;
  type: 'string' | 'number' | 'boolean' | 'currency' | 'date' | 'dateOnly' | 'fees';
  group?: string;
  frozen?: boolean;
  width?: string;
  /** When set, renders a color swatch dot using the value from this field on the row */
  colorField?: string;
}

// ── League ──

export const LEAGUE_COLUMNS: LadtColumnDef[] = [
  { field: 'leagueName', header: 'League', type: 'string', frozen: true, width: '180px' },
  { field: 'sportName', header: 'Sport', type: 'string', width: '140px' },
  { field: 'rescheduleEmailsToAddon', header: 'Reschedule Emails', type: 'string', width: '180px' },
  { field: 'bHideContacts', header: 'Hide Contacts', type: 'boolean', width: '70px' },
  { field: 'bHideStandings', header: 'Hide Standings', type: 'boolean', width: '70px' },
];

// ── Agegroup ──

export const AGEGROUP_COLUMNS: LadtColumnDef[] = [
  { field: 'agegroupName', header: 'Age Group', type: 'string', frozen: true, width: '180px', colorField: 'color' },
  { field: 'gender', header: 'Gender', type: 'string', width: '60px' },
  { field: '_fees', header: 'Fees', type: 'fees', width: '220px' },
  // Limits
  { field: 'maxTeams', header: 'Max Teams', type: 'number', group: 'Limits', width: '75px' },
  // Settings
  { field: 'bAllowSelfRostering', header: 'Self Roster', type: 'boolean', group: 'Settings', width: '70px' },
  { field: 'bChampionsByDivision', header: 'Champs by Div', type: 'boolean', group: 'Settings', width: '70px' },
  { field: 'bAllowApiRosterAccess', header: 'API Roster', type: 'boolean', group: 'Settings', width: '70px' },
  { field: 'bHideStandings', header: 'Hide Standings', type: 'boolean', group: 'Settings', width: '70px' },
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
  { field: 'active', header: 'Active', type: 'boolean', width: '70px' },
  { field: 'playerCount', header: 'Players', type: 'number', width: '75px' },
  { field: 'maxCount', header: 'Max Roster', type: 'number', width: '75px' },
  { field: '_fees', header: 'Fees', type: 'fees', width: '220px' },
  { field: 'divRank', header: 'Rank', type: 'number', width: '75px' },
  { field: 'divisionRequested', header: 'Div Requested', type: 'string', width: '140px' },
  { field: 'lastLeagueRecord', header: 'Last Record', type: 'string', width: '90px' },
  { field: 'levelOfPlay', header: 'LOP', type: 'string', width: '90px' },
  // Roster
  { field: 'bAllowSelfRostering', header: 'Self Roster', type: 'boolean', group: 'Roster', width: '70px' },
  { field: 'bHideRoster', header: 'Hide Roster', type: 'boolean', group: 'Roster', width: '70px' },
  // Dates
  { field: 'startdate', header: 'Start', type: 'date', group: 'Dates', width: '100px' },
  { field: 'enddate', header: 'End', type: 'date', group: 'Dates', width: '100px' },
  { field: 'effectiveasofdate', header: 'Effective', type: 'date', group: 'Dates', width: '100px' },
  { field: 'expireondate', header: 'Expires', type: 'date', group: 'Dates', width: '100px' },
  // Eligibility
  { field: 'gender', header: 'Gender', type: 'string', group: 'Eligibility', width: '60px' },
  // Advanced
  { field: 'requests', header: 'Requests', type: 'string', group: 'Advanced', width: '180px' },
  { field: 'teamComments', header: 'Comments', type: 'string', group: 'Advanced', width: '180px' },
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
