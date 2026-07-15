/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type CopyJobFormsResult = {
    success: boolean;
    playerCopied: boolean;
    coachCopied: boolean;
    pointerCopied?: boolean;
    optionsCopied?: boolean;
    sourceJobName: string;
    targetJobName?: string | null;
    errorMessage?: string | null;
};

