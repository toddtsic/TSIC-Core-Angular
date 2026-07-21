/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BracketMatchDto } from './BracketMatchDto';
import type { ConsolationGameDto } from './ConsolationGameDto';
export type DivisionBracketResponse = {
    agegroupName: string;
    divName: string;
    champion?: string | null;
    matches: Array<BracketMatchDto>;
    consolationGames?: Array<ConsolationGameDto>;
};

