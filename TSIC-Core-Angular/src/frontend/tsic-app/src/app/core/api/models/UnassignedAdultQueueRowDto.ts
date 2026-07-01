/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PriorStaffAssignmentDto } from './PriorStaffAssignmentDto';
import type { UnassignedAdultAssignedTeamDto } from './UnassignedAdultAssignedTeamDto';
import type { UnassignedAdultRecordedTeamDto } from './UnassignedAdultRecordedTeamDto';
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
    sportAssnId?: string | null;
    sportAssnIdexpDate?: string | null;
    idVerified: boolean;
    priorStaff: Array<PriorStaffAssignmentDto>;
    linkedPlayerNames: Array<string>;
    recordedTeams: Array<UnassignedAdultRecordedTeamDto>;
    assignedTeams: Array<UnassignedAdultAssignedTeamDto>;
};

