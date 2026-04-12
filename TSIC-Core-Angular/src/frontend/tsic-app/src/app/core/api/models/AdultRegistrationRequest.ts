/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AdultRoleType } from './AdultRoleType';
import type { CreditCardInfo } from './CreditCardInfo';
export type AdultRegistrationRequest = {
    username: string;
    password: string;
    firstName: string;
    lastName: string;
    email: string;
    phone: string;
    roleType: AdultRoleType;
    formValues?: any | null;
    waiverAcceptance?: any | null;
    creditCard?: (null | CreditCardInfo);
    paymentMethod?: string | null;
};

