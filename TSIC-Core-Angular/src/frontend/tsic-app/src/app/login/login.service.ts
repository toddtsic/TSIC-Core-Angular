import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class LoginService {
  constructor(private http: HttpClient) {}

  validateCredentials(username: string, password: string): Observable<any> {
    // Replace with actual API endpoint
    return this.http.post<any>('/api/auth/login', { username, password });
  }

  getJwtToken(username: string, regId: string): Observable<any> {
    // Replace with actual API endpoint
    return this.http.post<any>('/api/auth/token', { username, regId });
  }
}
