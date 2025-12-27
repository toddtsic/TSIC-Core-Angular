import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class LoginService {
  private readonly http = inject(HttpClient);

  validateCredentials(username: string, password: string): Observable<any> {
    const base = environment.apiUrl;
    return this.http.post<any>(`${base}/auth/login`, { username, password });
  }

  getJwtToken(username: string, regId: string): Observable<any> {
    const base = environment.apiUrl;
    return this.http.post<any>(`${base}/auth/token`, { username, regId });
  }
}
