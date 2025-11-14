/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AddressDto } from './AddressDto';
import type { ChildDto } from './ChildDto';
import type { PersonDto } from './PersonDto';
export type FamilyUpdateRequest = {
    username?: string | null;
    primary?: PersonDto;
    secondary?: PersonDto;
    address?: AddressDto;
    children?: Array<ChildDto> | null;
};

