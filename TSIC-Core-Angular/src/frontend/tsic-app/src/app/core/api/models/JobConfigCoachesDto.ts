/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AdultCoachProfileOptionDto } from './AdultCoachProfileOptionDto';
export type JobConfigCoachesDto = {
    bRegistrationAllowStaff: boolean | null;
    bRegistrationAllowReferee: boolean | null;
    bRegistrationAllowRecruiter: boolean | null;
    adultCoachProfileCode: string;
    adultCoachProfileName: string;
    adultCoachRequiresUsLax: boolean;
    availableAdultCoachProfiles: Array<AdultCoachProfileOptionDto>;
    adultRegConfirmationEmail: string | null;
    adultRegConfirmationOnScreen: string | null;
    adultRegRefundPolicy: string | null;
    adultRegReleaseOfLiability: string | null;
    adultRegCodeOfConduct: string | null;
    refereeRegConfirmationEmail: string | null;
    refereeRegConfirmationOnScreen: string | null;
    recruiterRegConfirmationEmail: string | null;
    recruiterRegConfirmationOnScreen: string | null;
};

