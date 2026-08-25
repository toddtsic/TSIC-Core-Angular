/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ArbNotifySkipDto } from './ArbNotifySkipDto';
import type { ArbRenderedEmailDto } from './ArbRenderedEmailDto';
export type AdnSweepResult = {
    checked: number;
    arbImported: number;
    echeckSettled: number;
    echeckReturnsProcessed: number;
    orphansFound: number;
    errored: number;
    failedDraftsFound?: number;
    failedDraftsEmailed?: number;
    failedDraftsNotEmailed?: number;
    succeeded: boolean;
    errorMessage?: string | null;
    digestHtml?: string | null;
    dryRun?: boolean;
    renderedEmails?: Array<ArbRenderedEmailDto>;
    notEmailed?: Array<ArbNotifySkipDto>;
    isTrustworthy?: boolean;
};

