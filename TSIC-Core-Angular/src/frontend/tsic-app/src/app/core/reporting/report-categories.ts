/**
 * Presentation-layer grouping for the reports library UI.
 *
 * The same code values live in `reporting.ReportCatalogue.CategoryCode`
 * (Type 2 / SP-driven reports, set in scripts/7-install-reporting-catalog.sql)
 * and in `Type1ReportEntry.category` (Type 1 / Crystal reports, hard-coded
 * in type1-report-catalog.ts). The library component groups by this field.
 *
 * `null` (Type 2 only) renders under the "Other" bucket. Type 1 entries are
 * required to declare a category — if you add one, pick the closest match.
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
