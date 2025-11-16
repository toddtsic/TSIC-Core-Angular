/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PreSubmitInsuranceDto } from './PreSubmitInsuranceDto';
import type { PreSubmitTeamResultDto } from './PreSubmitTeamResultDto';
import type { PreSubmitValidationErrorDto } from './PreSubmitValidationErrorDto';
export type PreSubmitRegistrationResponseDto = {
    teamResults?: Array<PreSubmitTeamResultDto> | null;
    readonly hasFullTeams?: boolean;
    nextTab?: string | null;
    insurance?: PreSubmitInsuranceDto;
    validationErrors?: Array<PreSubmitValidationErrorDto> | null;
};

