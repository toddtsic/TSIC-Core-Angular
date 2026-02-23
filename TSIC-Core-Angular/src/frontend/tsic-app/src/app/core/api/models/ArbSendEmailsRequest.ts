/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ArbFlagType } from './ArbFlagType';
export type ArbSendEmailsRequest = {
    jobId: string;
    senderUserId: string;
    flagType: ArbFlagType;
    emailSubject: string;
    emailBody: string;
    registrationIds: Array<string>;
    notifyDirectors?: boolean;
};

