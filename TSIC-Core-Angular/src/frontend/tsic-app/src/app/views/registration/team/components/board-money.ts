import type { RegisteredTeamDto } from '@core/api';

/**
 * The teams board's money (Todd 2026-09-27). One line per registered team, in the TEAM FEE — the
 * figure the register editor quoted — never the card total. Card-vs-check is the Payment step's
 * question, asked where the rep picks the method.
 *
 * The fee-only figure is `ckOwedTotal`: RegisteredTeamShaper's proc-free "still owed", anchored on
 * the stored OwedTotal (deposit phase: it IS DepositDue; full phase: it IS AdditionalDue). It
 * already carries any discount / late fee.
 *
 * Deliberately NOT the registered-teams grid's teamFeeStatusOf/sumDueNowOf: that grid sits on the
 * Payment step and the director's view beside per-method Owed columns, where the card total is
 * the right number.
 */
export type BoardFeeStatus =
    | { kind: 'waitlist' }
    | { kind: 'free' }
    /** Auto-pay drafts the card, so this one is the card amount — it is what will be drafted. */
    | { kind: 'scheduled'; owed: number; nextChargeDate: string | null }
    | { kind: 'depositDue'; owed: number; later: number }
    | { kind: 'depositPaid'; later: number }
    | { kind: 'balanceDue'; owed: number; depositPaid: boolean }
    | { kind: 'paid' };

const feeOwed = (t: RegisteredTeamDto) => Math.max(0, t.ckOwedTotal ?? 0);
const cardOwed = (t: RegisteredTeamDto) => Math.max(0, t.ccOwedTotal ?? 0);
/** Counted in "due now": not waiting on a slot, not drafted on its own schedule. */
const dueNowCounts = (t: RegisteredTeamDto) => !t.isWaitlisted && !(t.paymentScheduled && (t.owedTotal ?? 0) > 0);

export function boardFeeStatusOf(t: RegisteredTeamDto): BoardFeeStatus {
    if (t.isWaitlisted) return { kind: 'waitlist' };
    // A $0 team was never "paid in full" — nothing was ever asked.
    if ((t.feeTotal ?? 0) <= 0 && (t.paidTotal ?? 0) <= 0) return { kind: 'free' };
    if (t.paymentScheduled && (t.owedTotal ?? 0) > 0) {
        return { kind: 'scheduled', owed: t.owedTotal, nextChargeDate: t.nextChargeDate ?? null };
    }
    const owed = feeOwed(t);
    const depositPhase = !t.fullPaymentRequired && (t.deposit ?? 0) > 0 && (t.balanceDue ?? 0) > 0;
    if (depositPhase) {
        return owed > 0
            ? { kind: 'depositDue', owed, later: t.balanceDue }
            : { kind: 'depositPaid', later: t.balanceDue };
    }
    if (owed > 0) return { kind: 'balanceDue', owed, depositPaid: (t.tenderPaid ?? 0) > 0 };
    return { kind: 'paid' };
}

/** The sum of the rows' "due now" — fee only. The Continue card shows exactly this. */
export function sumFeeDueNowOf(teams: readonly RegisteredTeamDto[]): number {
    return teams.filter(dueNowCounts).reduce((s, t) => s + feeOwed(t), 0);
}

/** What paying the same teams by card adds on top — only to say that a card fee exists. */
export function sumCardFeeDueNowOf(teams: readonly RegisteredTeamDto[]): number {
    return teams.filter(dueNowCounts).reduce((s, t) => s + Math.max(0, cardOwed(t) - feeOwed(t)), 0);
}
