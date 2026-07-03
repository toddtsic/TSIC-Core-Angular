/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ReconciliationUnmatched } from './ReconciliationUnmatched';
export type ReconciliationStackSummary = {
    transactionCount: number;
    matchedCount: number;
    unmatchedCount: number;
    unmatchedTotal: number;
    paidCount: number;
    paidTotal: number;
    creditCount: number;
    creditTotal: number;
    unmatched: Array<ReconciliationUnmatched>;
};

