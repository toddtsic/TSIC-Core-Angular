/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BankAccountInfo } from './BankAccountInfo';
import type { CreditCardInfo } from './CreditCardInfo';
export type TeamArbTrialPaymentRequestDto = {
    teamIds: Array<string>;
    creditCard?: (null | CreditCardInfo);
    bankAccount?: (null | BankAccountInfo);
};

