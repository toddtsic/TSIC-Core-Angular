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
    registrations: RegistrationRoleDto[];
}

export interface RoleSelectionRequest {
    userId: string;
    regId: string;
}

export interface AuthenticatedUser {
    userId: string;
    username: string;
    firstName: string;
    lastName: string;
    selectedRole: string;
    jobPath: string;
}

export interface AuthTokenResponse {
    accessToken: string;
    expiresIn: number;
    user: AuthenticatedUser;
}
