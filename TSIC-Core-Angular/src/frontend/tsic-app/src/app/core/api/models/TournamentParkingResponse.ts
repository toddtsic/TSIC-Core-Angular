/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ParkingComplexDayDto } from './ParkingComplexDayDto';
import type { ParkingSummaryDto } from './ParkingSummaryDto';
import type { ParkingTimeslotDto } from './ParkingTimeslotDto';
export type TournamentParkingResponse = {
    rollup: Array<ParkingTimeslotDto>;
    complexDays: Array<ParkingComplexDayDto>;
    summary: ParkingSummaryDto;
};

