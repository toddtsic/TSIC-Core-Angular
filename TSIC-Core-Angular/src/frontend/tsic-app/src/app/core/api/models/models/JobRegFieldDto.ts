/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FieldCondition } from './FieldCondition';
import type { FieldValidation } from './FieldValidation';
export type JobRegFieldDto = {
    name: string;
    dbColumn: string;
    displayName: string;
    inputType: string;
    dataSource?: string | null;
    options?: any[] | null;
    validation?: (null | FieldValidation);
    order: number | string;
    visibility: string;
    computed: boolean;
    conditionalOn?: (null | FieldCondition);
};

