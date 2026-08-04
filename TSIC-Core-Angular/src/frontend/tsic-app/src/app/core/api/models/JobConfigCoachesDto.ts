/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AdultCoachProfileOptionDto } from './AdultCoachProfileOptionDto';
import type { AdultUsLaxMode } from './AdultUsLaxMode';
export type JobConfigCoachesDto = {
    bRegistrationAllowStaff: boolean | null;
    bRegistrationAllowReferee: boolean | null;
    bRegistrationAllowRecruiter: boolean | null;
    adultCoachProfileCode: string;
    adultCoachProfileName: string;
    adultCoachUsLax: AdultUsLaxMode;
    availableAdultCoachProfiles: Array<AdultCoachProfileOptionDto>;
    coachRegConfirmationEmail: string | null;
    coachRegConfirmationOnScreen: string | null;
    adultRegReleaseOfLiability: string | null;
    adultRegCodeOfConduct: string | null;
    refereeRegConfirmationEmail: string | null;
    refereeRegConfirmationOnScreen: string | null;
    recruiterRegConfirmationEmail: string | null;
    recruiterRegConfirmationOnScreen: string | null;
};

