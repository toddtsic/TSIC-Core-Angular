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
    jobLogo?: string;  // URL to job logo from registration
    // Legacy single role name (will be derived from roles[0] if present)
    role?: string;
    // New multi-role support (server currently emits single name; we normalize to array)
    roles?: string[];
}

// Bootstrap payload optionally returned with login to avoid extra API calls.
export interface FamilyUserSummary {
    familyUserId: string;
    displayName: string;
    avatarUrl?: string;
}

export type RegistrationStatus = 'none' | 'in-progress' | 'complete';
export type ProfileModel = 'PP' | 'CAC';

export interface PlayerJobRegistrationSummary {
    familyUserId: string;
    status: RegistrationStatus;
    regId?: string;
    profileModel: ProfileModel;
}

export interface LoginBootstrap {
    jobPath?: string; // job target if known from login context
    familyUsers?: FamilyUserSummary[];
    jobSummaries?: PlayerJobRegistrationSummary[]; // summaries specific to the jobPath
}

export interface AuthTokenResponse {
    accessToken: string;
    refreshToken?: string;
    expiresIn?: number;
    // Optional sidecar payload to prime the wizard without extra calls
    bootstrap?: LoginBootstrap;
}

export interface RefreshTokenRequest {
    refreshToken: string;
}
