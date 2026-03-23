/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CreditCardInfo } from './CreditCardInfo';
export type StoreCheckoutRequest = {
    paymentMethodId: string;
    creditCard?: (null | CreditCardInfo);
    comment?: string | null;
    discountCodeAi?: number | null;
};

