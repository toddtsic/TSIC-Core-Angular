import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { HelpContentDto, HelpManifestDto, SaveHelpContentRequest } from '@core/api';

/**
 * Context-sensitive help content. Reads are anonymous; the save is SuperUser + sandbox only
 * (the API returns 404 for a save on production). Content is authored as HTML fragments and
 * illustrated with the app's own design-system markup — see App_Data/Help on the backend.
 */
@Injectable({ providedIn: 'root' })
export class HelpService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/help`;

  /** The set of keys that actually have content — used to hide the "?" where there's nothing to show. */
  getManifest(): Observable<HelpManifestDto> {
    return this.http.get<HelpManifestDto>(`${this.apiUrl}/manifest`);
  }

  getContent(component: string, topic: string): Observable<HelpContentDto> {
    return this.http.get<HelpContentDto>(`${this.apiUrl}/${component}/${topic}`);
  }

  saveContent(component: string, topic: string, html: string): Observable<HelpContentDto> {
    const body: SaveHelpContentRequest = { html };
    return this.http.put<HelpContentDto>(`${this.apiUrl}/${component}/${topic}`, body);
  }
}
