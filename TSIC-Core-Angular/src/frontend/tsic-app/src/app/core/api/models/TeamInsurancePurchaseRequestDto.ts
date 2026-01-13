/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CreditCardInfo } from './CreditCardInfo';
export type TeamInsurancePurchaseRequestDto = {
    jobId: string;
    clubRepRegId: string;
    teamIds: Array<string>;
    quoteIds: Array<string>;
    creditCard?: (null | CreditCardInfo);
};

