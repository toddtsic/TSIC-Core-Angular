/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlayerRegInsuranceStatusDto } from './PlayerRegInsuranceStatusDto';
import type { PlayerRegTsicFinancialDto } from './PlayerRegTsicFinancialDto';
export type PlayerRegConfirmationDto = {
    tsic: PlayerRegTsicFinancialDto;
    insurance: PlayerRegInsuranceStatusDto;
    confirmationHtml: string;
};

