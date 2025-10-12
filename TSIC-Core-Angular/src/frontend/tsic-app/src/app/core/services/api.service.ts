import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  testConnection(): Observable<string> {
    return this.http.get(`/api/test`, { responseType: 'text' });
  }

  getHealth(): Observable<any> {
    return this.http.get(`/api/test/health`);
  }
}
