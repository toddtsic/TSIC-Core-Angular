/**
 * Canonical Level-of-Play choices — a FIXED 1–5 scale.
 *
 * LOP is intentionally NOT sourced from a job's `Jobs.JsonOptions → List_Lops`:
 * that field is per-job freeform and historically held junk ("10 players",
 * "competitive", "5 (strongest)", …). Every team-registration LOP control renders
 * these via the shared `LevelOfPlayPickerComponent` (the library fly-in's register
 * row + both create-modals); the admin Team Search edit form binds them into an
 * inline `<select>`. The stored value is always the bare digit ('1'..'5'); `short`
 * / `label` are display-only. Use `normalizeLop()` to coerce a stored/freeform
 * value back onto this scale.
 */
export interface LopChoice {
    /** Canonical stored value — bare digit '1'..'5'. */
    readonly value: string;
    /** Compact label for pill controls. */
    readonly short: string;
    /** Full label for selects / tooltips. */
    readonly label: string;
}

export const LOP_CHOICES: readonly LopChoice[] = [
    { value: '1', short: '1', label: '1' },
    { value: '2', short: '2', label: '2' },
    { value: '3', short: '3', label: '3' },
    { value: '4', short: '4', label: '4' },
    { value: '5', short: '5', label: '5 (strongest)' },
];

/**
 * Reconcile a stored/freeform LOP value to a canonical `LOP_CHOICES` value
 * ('1'..'5'), or '' when it doesn't map. Tolerates the friendly label form
 * ('5 (strongest)' → '5') but rejects out-of-scale junk ('10 players' → '',
 * 'Recreational' → '') so callers fall back to forcing an explicit pick.
 */
export function normalizeLop(value: string | null | undefined): string {
    if (!value) return '';
    const leadingInt = value.match(/^\s*(\d+)/);
    if (!leadingInt) return '';
    return LOP_CHOICES.some(c => c.value === leadingInt[1]) ? leadingInt[1] : '';
}

/**
 * LOP for DISPLAY — strip a numbered value's trailing modifier, pass anything else through
 * unchanged: '5 (strongest)' → '5', 'Recreational' → 'Recreational'.
 *
 * Distinct from `normalizeLop`, and the difference is the point: this one never hides a value.
 * `normalizeLop` answers "which pill is selected?" and returns '' for off-scale junk so a control
 * forces a real pick; `formatLop` answers "what does this row read as?", where blanking a legacy
 * value would silently lose data the user can see in the database. Read-only surfaces want this
 * one; editors want the other.
 */
export function formatLop(value: string | null | undefined): string {
    if (!value) return '';
    const match = value.match(/^\s*(\d+)/);
    return match ? match[1] : value;
}
