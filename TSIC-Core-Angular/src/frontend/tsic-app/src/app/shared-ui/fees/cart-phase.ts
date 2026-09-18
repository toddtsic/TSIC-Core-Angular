/**
 * Which payment phase a set of registered teams is in, and how to name it.
 *
 * One definition, three readers: the club rep's teams step and payment step (which both
 * showed this already) and the director's club-rep accounting card (which did not). That
 * absence was the bug: a director reads `Total Fee $27,600` directly above a ledger saying
 * `Total Fees $6,228` and nothing says the first is the full commitment while the second is
 * what has actually been billed. The club rep looking at the same $27,600 gets told.
 *
 * The rules below were settled on the rep's screens and are carried here unchanged — this is
 * an extraction, not a redesign.
 */

/**
 * A registered team, reduced to the three fields phase resolution actually reads.
 *
 * `isWaitlisted` is OPTIONAL to match the generated `RegisteredTeamDto`: it is a computed
 * get-only property on the C# record, so OpenAPI cannot mark it required and codegen emits
 * `isWaitlisted?: boolean`. Absent reads as not-waitlisted, which is exactly what the truthy
 * filter in the call sites this was extracted from already did.
 */
export interface CartPhaseRow {
    isWaitlisted?: boolean;
    fullPaymentRequired: boolean;
    deposit: number;
}

export type CartPhase = 'none' | 'deposit' | 'single' | 'full' | 'mixed';

/**
 * Resolve the phase of a cart.
 *
 * Waitlisted rows owe $0 and are excluded before anything else (PL-052 — two WL rows once
 * dragged an all-balance-due cart to "Deposit Only" through the mixed fallback).
 *
 * Deposit-less rows (deposit = 0, the canonical single-payment shape phase materialization
 * normalizes legacy tournaments into) have no phase to name: the one charge IS the whole fee.
 * They resolve to 'single' rather than 'deposit', because "Deposit Only" there promises a
 * second payment that never arrives.
 */
export function resolveCartPhase(rows: readonly CartPhaseRow[]): CartPhase {
    const teams = rows.filter(t => !t.isWaitlisted);
    if (!teams.length) return 'none';

    const full = teams.filter(t => t.fullPaymentRequired).length;
    if (full === 0) return teams.every(t => t.deposit <= 0) ? 'single' : 'deposit';
    if (full === teams.length) return 'full';
    return 'mixed';
}

/**
 * The badge text for a phase, or null when there is nothing honest to say.
 *
 * **The badge never reads "Mixed"** (PL-052, Ann's decision). When a cart's rows disagree —
 * or before any team is entered — a caller that knows the JOB's phase passes it as
 * `jobFullPaymentRequired` and that site-level fact is shown instead, because it is the one a
 * rep recognizes. A caller with no job flag to fall back on (the director's accounting card,
 * whose inputs are a registration id and a team id) gets null and renders no badge: silence
 * beats naming a phase we cannot substantiate.
 *
 * Measured 2026-09-18 across all 43 live jobs — 317 club reps, 9,389 teams — **no team
 * resolves to deposit phase and no rep holds teams spanning both**, so 'mixed' is a guard
 * against a shape the data has never taken, not a case anyone is hitting. `FullPaymentRequired`
 * also arrives from the server already resolved team → agegroup → league → job, so it can
 * never reach here ambiguous.
 */
export function cartPhaseBadgeLabel(
    phase: CartPhase,
    jobFullPaymentRequired?: boolean,
): string | null {
    switch (phase) {
        case 'full': return 'Final Balance Due';
        case 'single': return 'Single Payment';
        case 'deposit': return 'Deposit Only';
        default:
            if (jobFullPaymentRequired === undefined) return null;
            return jobFullPaymentRequired ? 'Final Balance Due' : 'Deposit Only';
    }
}
