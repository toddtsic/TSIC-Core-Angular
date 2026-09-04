/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RosterTransferWarningDto } from './RosterTransferWarningDto';
export type RosterTransferResultDto = {
    playersTransferred: number;
    staffCreated: number;
    staffDeleted: number;
    feesRecalculated: number;
    message: string;
    movedRegistrationIds: Array<string>;
    warnings: Array<RosterTransferWarningDto>;
};

