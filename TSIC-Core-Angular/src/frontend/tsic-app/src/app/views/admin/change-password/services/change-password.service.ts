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
  MergeCandidateDto,
  MergeUsernameRequest
} from '@core/api';

interface ApiMessage {
  message: string;
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

  resetPassword(regId: string, request: AdminResetPasswordRequest): Observable<ApiMessage> {
    return this.http.post<ApiMessage>(`${this.apiUrl}/${regId}/reset-password`, request);
  }

  resetFamilyPassword(regId: string, request: AdminResetPasswordRequest): Observable<ApiMessage> {
    return this.http.post<ApiMessage>(`${this.apiUrl}/${regId}/reset-family-password`, request);
  }

  updateUserEmail(regId: string, request: UpdateUserEmailRequest): Observable<ApiMessage> {
    return this.http.put<ApiMessage>(`${this.apiUrl}/${regId}/user-email`, request);
  }

  updateFamilyEmails(regId: string, request: UpdateFamilyEmailsRequest): Observable<ApiMessage> {
    return this.http.put<ApiMessage>(`${this.apiUrl}/${regId}/family-emails`, request);
  }

  getUserMergeCandidates(regId: string): Observable<MergeCandidateDto[]> {
    return this.http.get<MergeCandidateDto[]>(`${this.apiUrl}/${regId}/merge-candidates`);
  }

  getFamilyMergeCandidates(regId: string): Observable<MergeCandidateDto[]> {
    return this.http.get<MergeCandidateDto[]>(`${this.apiUrl}/${regId}/family-merge-candidates`);
  }

  mergeUsername(regId: string, request: MergeUsernameRequest): Observable<ApiMessage> {
    return this.http.post<ApiMessage>(`${this.apiUrl}/${regId}/merge-username`, request);
  }

  mergeFamilyUsername(regId: string, request: MergeUsernameRequest): Observable<ApiMessage> {
    return this.http.post<ApiMessage>(`${this.apiUrl}/${regId}/merge-family-username`, request);
  }
}
