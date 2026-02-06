/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PreSubmitInsuranceDto } from './PreSubmitInsuranceDto';
import type { PreSubmitTeamResultDto } from './PreSubmitTeamResultDto';
export type PreSubmitPlayerRegistrationResponseDto = {
    teamResults: Array<PreSubmitTeamResultDto>;
    hasFullTeams?: boolean;
    nextTab: string;
    insurance: (null | PreSubmitInsuranceDto);
    validationErrors: any[] | null;
};

