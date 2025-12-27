/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RegistrationFinancialsDto } from './RegistrationFinancialsDto';
export type FamilyPlayerRegistrationDto = {
    registrationId: string;
    active: boolean;
    financials: RegistrationFinancialsDto;
    assignedTeamId?: string | null;
    assignedTeamName?: string | null;
    adnSubscriptionId?: string | null;
    adnSubscriptionStatus?: string | null;
    adnSubscriptionAmountPerOccurence?: number | string | null;
    adnSubscriptionBillingOccurences?: number | string | null;
    adnSubscriptionIntervalLength?: number | string | null;
    adnSubscriptionStartDate?: string | null;
    formFieldValues: Record<string, any>;
};

