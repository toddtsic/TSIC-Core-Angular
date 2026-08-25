/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
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
    isTrustworthy?: boolean;
};

