import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
  ChangePasswordSearchRequest,
  ChangePasswordSearchResultDto,
  ChangePasswordRoleOptionDto,
  AdminResetPasswordRequest,
  UpdateUserEmailRequest,
  UpdateFamilyEmailsRequest,
  MergeCandidatesResponse,
  MergeUsernameRequest
} from '@core/api';

export interface ApiMessage {
  message: string;
}

/**
 * Which of a registration's two accounts a reset targets. `Registrations` points into `AspNetUsers`
 * twice — `UserId` (who the registration is about) and `Family_UserId` (the login that owns it).
 * The generated `ResetPasswordTarget` is a bare `number`, so name the values here.
 * See docs/Domain/change-password-contract.md §1.
 */
export const ResetTarget = {
  /** The registrant's own login. Meaningful for adults; a player's is vestigial. */
  User: 0,
  /** The family login — the only login a parent ever uses. */
  Family: 1
} as const;

/**
 * "This person has no email address" — a recorded fact, as opposed to a blank, which only says we
 * never captured one. Both are excluded from every send.
 *
 * The button writes it so nobody types it; a typo'd marker is just a live address that bounces.
 * Mirrors `EmailAddressRules.NotGiven` (C#).
 */
export const NO_EMAIL_SENTINEL = 'not@given.com';

@Injectable({ providedIn: 'root' })
export class ChangePasswordService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/change-password`;

  getRoleOptions(): Observable<ChangePasswordRoleOptionDto[]> {
    return this.http.get<ChangePasswordRoleOptionDto[]>(`${this.apiUrl}/role-options`);
  }

  search(request: ChangePasswordSearchRequest): Observable<ChangePasswordSearchResultDto[]> {
    return this.http.post<ChangePasswordSearchResultDto[]>(`${this.apiUrl}/search`, request);
  }

  /**
   * ONE reset endpoint. The account is resolved server-side from the registration's own FK; the
   * request only says which FK to follow. The old `reset-family-password` was a byte-for-byte
   * duplicate of this and both ignored the regId entirely.
   */
  resetPassword(regId: string, request: AdminResetPasswordRequest): Observable<ApiMessage> {
    return this.http.post<ApiMessage>(`${this.apiUrl}/${regId}/reset-password`, request);
  }

  updateUserEmail(regId: string, request: UpdateUserEmailRequest): Observable<ApiMessage> {
    return this.http.put<ApiMessage>(`${this.apiUrl}/${regId}/user-email`, request);
  }

  updateFamilyEmails(regId: string, request: UpdateFamilyEmailsRequest): Observable<ApiMessage> {
    return this.http.put<ApiMessage>(`${this.apiUrl}/${regId}/family-emails`, request);
  }

  getUserMergeCandidates(regId: string): Observable<MergeCandidatesResponse> {
    return this.http.get<MergeCandidatesResponse>(`${this.apiUrl}/${regId}/merge-candidates`);
  }

  getFamilyMergeCandidates(regId: string): Observable<MergeCandidatesResponse> {
    return this.http.get<MergeCandidatesResponse>(`${this.apiUrl}/${regId}/family-merge-candidates`);
  }

  mergeUsername(regId: string, request: MergeUsernameRequest): Observable<ApiMessage> {
    return this.http.post<ApiMessage>(`${this.apiUrl}/${regId}/merge-username`, request);
  }

  mergeFamilyUsername(regId: string, request: MergeUsernameRequest): Observable<ApiMessage> {
    return this.http.post<ApiMessage>(`${this.apiUrl}/${regId}/merge-family-username`, request);
  }
}
