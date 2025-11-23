/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type InsurancePurchaseRequestDto = {
    jobId: string;
    familyUserId: string;
    registrationIds: Array<string>;
    quoteIds: Array<string>;
    creditCard?: {
        Number?: string | null;
        Expiry?: string | null; // MMYY
        Code?: string | null;
        FirstName?: string | null;
        LastName?: string | null;
        Zip?: string | null;
        Email?: string | null;
        Phone?: string | null;
        Address?: string | null;
    } | null;
};

