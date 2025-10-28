export interface LoginRequest {
    username: string;
    password: string;
}

export interface RegistrationDto {
    regId: string;
    displayText: string;
    jobLogo: string;
}

export interface RegistrationRoleDto {
    roleName: string;
    roleRegistrations: RegistrationDto[];
}

export interface LoginResponse {
    userId: string;
    registrations: RegistrationRoleDto[];
}

export interface RoleSelectionRequest {
    regId: string;
}

export interface AuthenticatedUser {
    username: string;
    regId?: string;
    jobPath?: string;
}

export interface AuthTokenResponse {
    accessToken: string;
    expiresIn?: number;
}
