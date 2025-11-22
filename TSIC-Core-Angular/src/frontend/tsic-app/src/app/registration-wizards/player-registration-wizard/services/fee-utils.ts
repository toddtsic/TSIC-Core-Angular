export function computeFee(prFee: number | null = null, teamFee: number | null = null, rosterFee: number | null = null): number {
    const f = prFee ?? 0;
    const t = teamFee ?? 0;
    const r = rosterFee ?? 0;
    if (f > 0) return round2(f);
    if (t > 0 && r > 0) return round2(t);
    if (r > 0) return round2(r);
    return 0;
}

export function computeDeposit(prDeposit: number | null = null, teamFee: number | null = null, rosterFee: number | null = null): number {
    const d = prDeposit ?? 0;
    const t = teamFee ?? 0;
    const r = rosterFee ?? 0;
    if (d > 0) return round2(d);
    if (t > 0 && r > 0) return round2(r);
    return 0;
}

function round2(v: number): number { return Math.round((v + Number.EPSILON) * 100) / 100; }
