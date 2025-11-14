/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { VIOrganizationDto } from './VIOrganizationDto';
import type { VIParticipantDto } from './VIParticipantDto';
export type VIPlayerPolicyAttributes = {
    event_start_date?: string | null;
    event_end_date?: string | null;
    insurable_amount?: number;
    participant?: VIParticipantDto;
    organization?: VIOrganizationDto;
};

