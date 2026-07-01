/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AdultWaiverDto } from './AdultWaiverDto';
import type { JobRegFieldDto } from './JobRegFieldDto';
export type AdultRoleConfigDto = {
    roleKey: string;
    displayName: string;
    description: string;
    icon: string;
    needsTeamSelection: boolean;
    allowTeamRequests: boolean;
    profileFields: Array<JobRegFieldDto>;
    waivers: Array<AdultWaiverDto>;
};

