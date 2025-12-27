import { computeFee, computeDeposit } from './fee-utils';

describe('fee-utils', () => {
    it('prefers per-registrant fee when > 0', () => {
        expect(computeFee(80, 150, 100)).toBe(80);
    });
    it('falls back to team fee when both team and roster > 0 and perRegistrant fee <= 0', () => {
        expect(computeFee(0, 150, 100)).toBe(150);
        expect(computeFee(null, 200, 50)).toBe(200);
    });
    it('falls back to roster fee when only roster fee > 0', () => {
        expect(computeFee(undefined, 0, 90)).toBe(90);
    });
    it('returns 0 when all inputs <= 0 or undefined', () => {
        expect(computeFee()).toBe(0);
        expect(computeFee(null, null, null)).toBe(0);
        expect(computeFee(-1, -2, -3)).toBe(0);
    });
    it('deposit prefers explicit per-registrant deposit', () => {
        expect(computeDeposit(30, 150, 100)).toBe(30);
    });
    it('deposit falls back to roster fee when team + roster > 0 and no explicit deposit', () => {
        expect(computeDeposit(0, 150, 100)).toBe(100);
        expect(computeDeposit(null, 10, 55)).toBe(55);
    });
    it('deposit returns 0 when no qualifying values', () => {
        expect(computeDeposit()).toBe(0);
        expect(computeDeposit(null, null, null)).toBe(0);
        expect(computeDeposit(-1, 0, -5)).toBe(0);
    });
    it('rounds to two decimals', () => {
        expect(computeFee(80.1234, 0, 0)).toBe(80.12);
        expect(computeDeposit(30.6789, 0, 0)).toBe(30.68);
    });
});
