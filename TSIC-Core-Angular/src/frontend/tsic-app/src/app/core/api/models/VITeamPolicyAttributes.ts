/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { VIEventDto } from './VIEventDto';
import type { VITeamDto } from './VITeamDto';
export type VITeamPolicyAttributes = {
    organization_name?: string;
    organization_contact_name?: string;
    organization_contact_email?: string;
    teams?: Array<VITeamDto>;
    event?: VIEventDto;
};

