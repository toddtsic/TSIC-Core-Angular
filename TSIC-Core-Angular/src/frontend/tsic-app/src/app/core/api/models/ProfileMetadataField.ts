/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FieldCondition } from './FieldCondition';
import type { FieldValidation } from './FieldValidation';
import type { ProfileFieldOption } from './ProfileFieldOption';
export type ProfileMetadataField = {
    name?: string | null;
    dbColumn?: string | null;
    displayName?: string | null;
    inputType?: string | null;
    dataSource?: string | null;
    options?: Array<ProfileFieldOption> | null;
    validation?: FieldValidation;
    order?: number;
    visibility?: string | null;
    /**
     * @deprecated
     */
    adminOnly?: boolean;
    computed?: boolean;
    conditionalOn?: FieldCondition;
};

