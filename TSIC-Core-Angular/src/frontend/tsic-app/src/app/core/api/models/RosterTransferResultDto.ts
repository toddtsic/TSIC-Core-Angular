/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RosterTransferBlockedDto } from './RosterTransferBlockedDto';
export type RosterTransferResultDto = {
    playersTransferred: number;
    staffCreated: number;
    staffDeleted: number;
    feesRecalculated: number;
    message: string;
    movedRegistrationIds: Array<string>;
    blocked: Array<RosterTransferBlockedDto>;
};

