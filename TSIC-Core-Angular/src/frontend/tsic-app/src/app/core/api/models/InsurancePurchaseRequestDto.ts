/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CreditCardInfo } from './CreditCardInfo';
export type InsurancePurchaseRequestDto = {
    jobId: string;
    familyUserId: string;
    registrationIds: Array<string>;
    quoteIds: Array<string>;
    creditCard?: (null | CreditCardInfo);
};

