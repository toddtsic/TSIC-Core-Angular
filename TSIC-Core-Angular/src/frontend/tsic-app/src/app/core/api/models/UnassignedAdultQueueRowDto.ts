/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PriorStaffAssignmentDto } from './PriorStaffAssignmentDto';
import type { UnassignedAdultRequestDto } from './UnassignedAdultRequestDto';
export type UnassignedAdultQueueRowDto = {
    registrationId: string;
    playerName: string;
    clubName?: string | null;
    email?: string | null;
    cellphone?: string | null;
    city?: string | null;
    state?: string | null;
    registrationTs: string;
    note?: string | null;
    priorStaff: Array<PriorStaffAssignmentDto>;
    linkedPlayerNames: Array<string>;
    pendingTeams: Array<UnassignedAdultRequestDto>;
};

