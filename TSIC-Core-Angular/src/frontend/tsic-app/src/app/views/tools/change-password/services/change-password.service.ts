import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
  ChangePasswordSearchRequest,
  ChangePasswordSearchResultDto,
  ChangePasswordRoleOptionDto,
  AdminResetPasswordRequest,
  ResetContextDto,
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
 * never captured one. It is a flag, not an address, so it is never DISPLAYED as one.
 * Mirrors `EmailAddressRules.NotGiven` (C#).
 */
export const NO_EMAIL_SENTINEL = 'not@given.com';

/**
 * `not@given.com` is a FLAG — "we asked, there isn't one" — not an address. Legacy stripped it on the
 * way to the screen and so do we: rendered as an address, it invites someone to try to mail it.
 */
export function displayEmail(email: string | null | undefined): string {
  if (!email) return '—';
  return email.trim().toLowerCase() === NO_EMAIL_SENTINEL ? '—' : email;
}

/**
 * `2008-03-28`, straight off the wire. Deliberately NOT the date pipe: a DOB is a calendar fact with
 * no timezone, and the pipe renders it in the browser's, which moves it a day for anyone west of UTC.
 * It is on screen to tell two children apart, so a day matters.
 */
export function dobLabel(value: string | null | undefined): string {
  return value ? value.slice(0, 10) : '—';
}

/**
 * How the server keys a child — case-folded name + exact DOB
 * (`ChangePasswordRepository.BuildChildCollapseAsync`). Keep the two in step.
 */
export function childKey(name: string, dob: string | null | undefined): string {
  return `${name.trim().toLowerCase()}|${dob ? dob.slice(0, 10) : ''}`;
}

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
   * What the reset dialog shows BEFORE anyone types: the account this registration actually resolves
   * to, whose it is, and what it signs in for. For a player row that account is the FAMILY's.
   */
  getResetContext(regId: string, target: number): Observable<ResetContextDto> {
    const params = new HttpParams().set('target', target);
    return this.http.get<ResetContextDto>(`${this.apiUrl}/${regId}/reset-context`, { params });
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

  /** Adult logins that are the same person IN THE SAME ROLE. Empty for a player. */
  getUserMergeCandidates(regId: string): Observable<MergeCandidatesResponse> {
    return this.http.get<MergeCandidatesResponse>(`${this.apiUrl}/${regId}/merge-candidates`);
  }

  /** Family logins that are the same household — the mother's email, phone and name all agree. */
  getFamilyMergeCandidates(regId: string): Observable<MergeCandidatesResponse> {
    return this.http.get<MergeCandidatesResponse>(`${this.apiUrl}/${regId}/family-merge-candidates`);
  }

  /** Retire ONE adult login onto another. Irreversible. */
  mergeUsername(regId: string, request: MergeUsernameRequest): Observable<ApiMessage> {
    return this.http.post<ApiMessage>(`${this.apiUrl}/${regId}/merge-username`, request);
  }

  /** Retire ONE family login onto another. Irreversible — it moves a household's children. */
  mergeFamilyUsername(regId: string, request: MergeUsernameRequest): Observable<ApiMessage> {
    return this.http.post<ApiMessage>(`${this.apiUrl}/${regId}/merge-family-username`, request);
  }
}
