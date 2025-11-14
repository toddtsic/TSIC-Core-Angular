/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FamilyPlayerRegistrationDto } from './FamilyPlayerRegistrationDto';
export type FamilyPlayerDto = {
    playerId?: string | null;
    firstName?: string | null;
    lastName?: string | null;
    gender?: string | null;
    dob?: string | null;
    registered?: boolean;
    selected?: boolean;
    priorRegistrations?: Array<FamilyPlayerRegistrationDto> | null;
};

