/**
 * Presentation-layer grouping for the reports library UI.
 *
 * The same seven code values live in `reporting.ReportLibrary.CategoryCode` (CHECK-constrained,
 * scripts/23-install-reporting-library.sql) and, as `GroupLabel`, on every shelf row in
 * `reporting.JobReports` (normalised by scripts/8). The library component groups by this field
 * in both modes. Anything else renders under the "Other" bucket.
 */

export type ReportCategory =
    | 'Rosters'
    | 'Schedules'
    | 'Registrations'
    | 'Financials'
    | 'Camp'
    | 'Recruiting'
    | 'Administration';

export interface ReportCategoryMeta {
    readonly code: ReportCategory;
    readonly label: string;
    readonly iconName: string;
    readonly sortOrder: number;
}

export const REPORT_CATEGORIES: readonly ReportCategoryMeta[] = [
    { code: 'Rosters',        label: 'Rosters',        iconName: 'people',            sortOrder: 10 },
    { code: 'Schedules',      label: 'Schedules',      iconName: 'calendar3',         sortOrder: 20 },
    { code: 'Registrations',  label: 'Registrations',  iconName: 'card-checklist',    sortOrder: 30 },
    { code: 'Financials',     label: 'Financials',     iconName: 'cash-coin',         sortOrder: 40 },
    { code: 'Camp',           label: 'Camp',           iconName: 'backpack',          sortOrder: 50 },
    { code: 'Recruiting',     label: 'Recruiting',     iconName: 'mortarboard',       sortOrder: 60 },
    { code: 'Administration', label: 'Administration', iconName: 'gear',              sortOrder: 70 },
];

export const UNCATEGORIZED_META: ReportCategoryMeta = {
    code: 'Administration', // unused; placeholder for typing
    label: 'Other',
    iconName: 'three-dots',
    sortOrder: 999,
};

const CATEGORY_BY_CODE: ReadonlyMap<string, ReportCategoryMeta> =
    new Map(REPORT_CATEGORIES.map(c => [c.code, c]));

export function getCategoryMeta(code: string | null | undefined): ReportCategoryMeta {
    if (!code) return UNCATEGORIZED_META;
    return CATEGORY_BY_CODE.get(code) ?? UNCATEGORIZED_META;
}

/**
 * Returns the value if it's a valid category code, else null (→ "Other" bucket).
 * Legacy GroupLabels (e.g. 'Reports') aren't codes; without this they get bucketed
 * under a key the grouped view never renders — counted in tabs but invisible.
 */
export function normalizeReportCategory(raw: string | null | undefined): ReportCategory | null {
    return raw != null && CATEGORY_BY_CODE.has(raw) ? (raw as ReportCategory) : null;
}
