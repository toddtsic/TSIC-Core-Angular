/**
 * Column definitions for the LADT sibling comparison grids.
 * Each level (league, agegroup, division, team) defines its own columns
 * that map DTO field names to human-readable headers with display metadata.
 */

export interface LadtColumnDef {
  field: string;
  header: string;
  type: 'string' | 'number' | 'boolean' | 'currency' | 'date' | 'dateOnly';
  group?: string;
  frozen?: boolean;
  width?: string;
  /** When set, renders a color swatch dot using the value from this field on the row */
  colorField?: string;
}

// ── League ──

export const LEAGUE_COLUMNS: LadtColumnDef[] = [
  { field: 'leagueName', header: 'League', type: 'string', frozen: true, width: '180px' },
  { field: 'sportName', header: 'Sport', type: 'string' },
  { field: 'playerFeeOverride', header: 'Player Fee Override', type: 'currency' },
  { field: 'rescheduleEmailsToAddon', header: 'Reschedule Emails', type: 'string' },
  { field: 'bHideContacts', header: 'Hide Contacts', type: 'boolean' },
  { field: 'bHideStandings', header: 'Hide Standings', type: 'boolean' },
];

// ── Agegroup ──

export const AGEGROUP_COLUMNS: LadtColumnDef[] = [
  { field: 'agegroupName', header: 'Age Group', type: 'string', frozen: true, width: '180px', colorField: 'color' },
  { field: 'color', header: 'Color', type: 'string' },
  { field: 'gender', header: 'Gender', type: 'string' },
  { field: 'sortAge', header: 'Sort', type: 'number' },
  // Fees
  { field: 'teamFee', header: 'Team Fee', type: 'currency', group: 'Fees' },
  { field: 'teamFeeLabel', header: 'Team Fee Label', type: 'string', group: 'Fees' },
  { field: 'rosterFee', header: 'Roster Fee', type: 'currency', group: 'Fees' },
  { field: 'rosterFeeLabel', header: 'Roster Fee Label', type: 'string', group: 'Fees' },
  { field: 'discountFee', header: 'Discount Fee', type: 'currency', group: 'Fees' },
  { field: 'discountFeeStart', header: 'Discount Start', type: 'date', group: 'Fees' },
  { field: 'discountFeeEnd', header: 'Discount End', type: 'date', group: 'Fees' },
  { field: 'lateFee', header: 'Late Fee', type: 'currency', group: 'Fees' },
  { field: 'lateFeeStart', header: 'Late Start', type: 'date', group: 'Fees' },
  { field: 'lateFeeEnd', header: 'Late End', type: 'date', group: 'Fees' },
  { field: 'playerFeeOverride', header: 'Player Fee Override', type: 'currency', group: 'Fees' },
  // Limits
  { field: 'maxTeams', header: 'Max Teams', type: 'number', group: 'Limits' },
  { field: 'maxTeamsPerClub', header: 'Max/Club', type: 'number', group: 'Limits' },
  // Settings
  { field: 'bAllowSelfRostering', header: 'Self Roster', type: 'boolean', group: 'Settings' },
  { field: 'bChampionsByDivision', header: 'Champs by Div', type: 'boolean', group: 'Settings' },
  { field: 'bAllowApiRosterAccess', header: 'API Roster', type: 'boolean', group: 'Settings' },
  { field: 'bHideStandings', header: 'Hide Standings', type: 'boolean', group: 'Settings' },
];

// ── Division ──

export const DIVISION_COLUMNS: LadtColumnDef[] = [
  { field: 'divName', header: 'Division', type: 'string', frozen: true, width: '180px' },
  { field: 'maxRoundNumberToShow', header: 'Max Round#', type: 'number' },
];

// ── Team ──

export const TEAM_COLUMNS: LadtColumnDef[] = [
  { field: 'clubName', header: 'Club', type: 'string', frozen: true, width: '160px' },
  { field: 'teamName', header: 'Team', type: 'string', frozen: true, width: '160px' },
  { field: 'active', header: 'Active', type: 'boolean' },
  { field: 'playerCount', header: 'Players', type: 'number' },
  { field: 'divRank', header: 'Rank', type: 'number' },
  { field: 'divisionRequested', header: 'Div Requested', type: 'string' },
  { field: 'lastLeagueRecord', header: 'Last Record', type: 'string' },
  { field: 'levelOfPlay', header: 'LOP', type: 'string' },
  // Roster
  { field: 'maxCount', header: 'Max Roster', type: 'number', group: 'Roster' },
  { field: 'bAllowSelfRostering', header: 'Self Roster', type: 'boolean', group: 'Roster' },
  { field: 'bHideRoster', header: 'Hide Roster', type: 'boolean', group: 'Roster' },
  // Fees
  { field: 'feeBase', header: 'Base Fee', type: 'currency', group: 'Fees' },
  { field: 'perRegistrantFee', header: 'Per-Player Fee', type: 'currency', group: 'Fees' },
  { field: 'perRegistrantDeposit', header: 'Per-Player Deposit', type: 'currency', group: 'Fees' },
  { field: 'discountFee', header: 'Discount Fee', type: 'currency', group: 'Fees' },
  { field: 'discountFeeStart', header: 'Discount Start', type: 'date', group: 'Fees' },
  { field: 'discountFeeEnd', header: 'Discount End', type: 'date', group: 'Fees' },
  { field: 'lateFee', header: 'Late Fee', type: 'currency', group: 'Fees' },
  { field: 'lateFeeStart', header: 'Late Start', type: 'date', group: 'Fees' },
  { field: 'lateFeeEnd', header: 'Late End', type: 'date', group: 'Fees' },
  // Dates
  { field: 'startdate', header: 'Start', type: 'date', group: 'Dates' },
  { field: 'enddate', header: 'End', type: 'date', group: 'Dates' },
  { field: 'effectiveasofdate', header: 'Effective', type: 'date', group: 'Dates' },
  { field: 'expireondate', header: 'Expires', type: 'date', group: 'Dates' },
  // Eligibility
  { field: 'gender', header: 'Gender', type: 'string', group: 'Eligibility' },
  // Advanced
  { field: 'requests', header: 'Requests', type: 'string', group: 'Advanced' },
  { field: 'keywordPairs', header: 'Keywords', type: 'string', group: 'Advanced' },
  { field: 'teamComments', header: 'Comments', type: 'string', group: 'Advanced' },
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
