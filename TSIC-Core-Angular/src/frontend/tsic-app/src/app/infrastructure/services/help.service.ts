import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, catchError, map } from 'rxjs';
import { HelpContent, HelpManifest } from './help.types';
import { HelpFsService } from './help-fs.service';

/**
 * Context-sensitive help content, served as static assets from public/help. It travels with the
 * frontend build (no API dependency), lives next to the design-system styles it renders with, and is
 * validated against the route table at build time (scripts/verify-help.mjs). Reads are plain HTTP GETs
 * for the static files; the dev-only save writes the working-tree file via the File System Access API.
 * There is deliberately no server — production is read-only by construction.
 */
@Injectable({ providedIn: 'root' })
export class HelpService {
  private readonly http = inject(HttpClient);
  private readonly fs = inject(HelpFsService);
  private readonly base = 'help';

  /** The keys that actually have content — drives "?" visibility. Fails closed to an empty set. */
  getManifest(): Observable<HelpManifest> {
    return this.http
      .get<HelpManifest>(`/${this.base}/manifest.json`)
      .pipe(catchError(() => of({ keys: [] as string[] })));
  }

  /** Fetch one topic's HTML. Callers gate on the manifest, so a missing file is never requested. */
  getContent(component: string, topic: string): Observable<HelpContent> {
    return this.http
      .get(`/${this.base}/${component}/${topic}.html`, { responseType: 'text' })
      .pipe(map((html) => ({ component, topic, html, exists: true })));
  }

  /** Dev-only save (local development). Writes the working-tree file; author commits & pushes it. */
  saveContent(component: string, topic: string, html: string): Promise<void> {
    return this.fs.write(component, topic, html);
  }
}
