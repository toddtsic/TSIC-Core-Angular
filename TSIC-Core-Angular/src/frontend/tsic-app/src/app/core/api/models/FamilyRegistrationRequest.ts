/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AddressDto } from './AddressDto';
import type { ChildDto } from './ChildDto';
import type { PersonDto } from './PersonDto';
export type FamilyRegistrationRequest = {
    username: string;
    password: string;
    primary: PersonDto;
    secondary: PersonDto;
    address: AddressDto;
    children: Array<ChildDto>;
};

