/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { LedgerSplit } from './LedgerSplit';
export type LedgerEntry = {
    date: string;
    type: string;
    party: string;
    account: string;
    amount: number;
    docNum: string;
    memo: string;
    splits: Array<LedgerSplit>;
};

