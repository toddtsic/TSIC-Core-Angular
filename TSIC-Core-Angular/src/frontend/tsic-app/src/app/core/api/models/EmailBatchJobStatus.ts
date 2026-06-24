/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type EmailBatchJobStatus = {
    jobId: string;
    totalRecipients: number;
    sent: number;
    failed: number;
    optedOut: number;
    done: boolean;
    failedAddresses: Array<string>;
    processed?: number;
};

