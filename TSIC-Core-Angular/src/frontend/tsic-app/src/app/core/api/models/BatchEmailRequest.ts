/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type BatchEmailRequest = {
    registrationIds: Array<string>;
    subject: string;
    bodyTemplate: string;
    inviteLinkTargetJobId?: string | null;
    inviteExpiryHours?: number | null;
    simulatedPerUnitDelayMs?: number | null;
    sandboxTestRecipient?: string | null;
};

