/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RosterTableColumnDto } from './RosterTableColumnDto';
export type RosterTableRequestDto = {
    groupBy: string;
    sortBy: string;
    columns: Array<RosterTableColumnDto>;
    orientation: string;
    playersOnly: boolean;
    pageBreakPerGroup: boolean;
    colorAccent: boolean;
};

