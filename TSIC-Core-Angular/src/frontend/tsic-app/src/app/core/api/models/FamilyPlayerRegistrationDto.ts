/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RegistrationFinancialsDto } from './RegistrationFinancialsDto';
export type FamilyPlayerRegistrationDto = {
    registrationId?: string;
    active?: boolean;
    financials?: RegistrationFinancialsDto;
    assignedTeamId?: string | null;
    assignedTeamName?: string | null;
    formFieldValues?: Record<string, any> | null;
};

