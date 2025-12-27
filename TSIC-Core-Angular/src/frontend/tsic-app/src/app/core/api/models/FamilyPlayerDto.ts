/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FamilyPlayerRegistrationDto } from './FamilyPlayerRegistrationDto';
export type FamilyPlayerDto = {
    playerId: string;
    firstName: string;
    lastName: string;
    gender: string;
    dob?: string | null;
    registered: boolean;
    selected: boolean;
    priorRegistrations: Array<FamilyPlayerRegistrationDto>;
    defaultFieldValues?: any | null;
};

