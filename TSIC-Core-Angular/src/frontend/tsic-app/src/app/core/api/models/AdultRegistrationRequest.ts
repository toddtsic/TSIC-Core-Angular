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
    gender: string;
    email: string;
    phone: string;
    cellphoneProvider?: string | null;
    streetAddress: string;
    city: string;
    state: string;
    postalCode: string;
    roleKey: string;
    roleType?: (null | AdultRoleType);
    acceptedTos: boolean;
    formValues?: any | null;
    waiverAcceptance?: any | null;
    teamIdsCoaching?: any[] | null;
    creditCard?: (null | CreditCardInfo);
    paymentMethod?: string | null;
};

