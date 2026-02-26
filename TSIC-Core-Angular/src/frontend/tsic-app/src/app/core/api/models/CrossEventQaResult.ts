/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CrossEventClubOverplay } from './CrossEventClubOverplay';
import type { CrossEventJobInfo } from './CrossEventJobInfo';
import type { CrossEventTeamOverplay } from './CrossEventTeamOverplay';
export type CrossEventQaResult = {
    groupName: string;
    comparedEvents: Array<CrossEventJobInfo>;
    clubOverplay: Array<CrossEventClubOverplay>;
    teamOverplay: Array<CrossEventTeamOverplay>;
};

