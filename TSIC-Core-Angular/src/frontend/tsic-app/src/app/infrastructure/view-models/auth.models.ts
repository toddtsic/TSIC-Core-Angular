import type { FamilyUserSummaryDto as FamilyUserSummary } from '@core/api';
// Re-export server-generated auth models to avoid duplication
export type { LoginRequest, RegistrationDto, RegistrationRoleDto, RoleSelectionRequest, RefreshTokenRequest, AuthTokenResponse } from '@core/api';
// Keep existing LoginResponse name by aliasing the generated DTO
export type { LoginResponseDto as LoginResponse } from '@core/api';

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
// Alias server DTO to existing name
export type { FamilyUserSummaryDto as FamilyUserSummary } from '@core/api';

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

// Extend AuthTokenResponse at usage sites if we attach optional bootstrap locally
