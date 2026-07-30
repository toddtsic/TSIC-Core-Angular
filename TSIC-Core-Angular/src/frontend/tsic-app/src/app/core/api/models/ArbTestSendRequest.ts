/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ArbFlagType } from './ArbFlagType';
export type ArbTestSendRequest = {
    jobId: string;
    flagType: ArbFlagType;
    registrationId: string;
    emailSubject: string;
    emailBody: string;
    includeSuperusers?: boolean;
    extraRecipient?: string | null;
};

