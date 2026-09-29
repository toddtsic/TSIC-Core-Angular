/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TournamentTeamOptionDto } from './TournamentTeamOptionDto';
export type TeamTournamentMatchDto = {
    tournamentJobId: string;
    tournamentJobName: string;
    matchedTeamId?: string | null;
    candidates: Array<TournamentTeamOptionDto>;
};

