/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { LedgerEntry } from './LedgerEntry';
export type LedgerTab = {
    name: string;
    stack: string;
    kind: string;
    columns: Array<string>;
    rows: Array<Array<string>>;
    entries: Array<LedgerEntry>;
};

