/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type NavEditorNavItemDto = {
    navItemId: number;
    navId: number;
    parentNavItemId?: number;
    sortOrder: number;
    text: string;
    iconName?: string | null;
    routerLink?: string | null;
    navigateUrl?: string | null;
    target?: string | null;
    active: boolean;
    children: Array<NavEditorNavItemDto>;
};

