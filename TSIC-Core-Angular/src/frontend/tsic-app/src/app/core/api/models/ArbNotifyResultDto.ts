/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ArbAuditRowDto } from './ArbAuditRowDto';
import type { ArbNotifySkipDto } from './ArbNotifySkipDto';
import type { ArbRenderedEmailDto } from './ArbRenderedEmailDto';
export type ArbNotifyResultDto = {
    found: number;
    emailed: number;
    skipped: number;
    skips: Array<ArbNotifySkipDto>;
    dryRun?: boolean;
    rendered?: Array<ArbRenderedEmailDto>;
    auditRows?: Array<ArbAuditRowDto>;
    summaryHtml?: string | null;
};

