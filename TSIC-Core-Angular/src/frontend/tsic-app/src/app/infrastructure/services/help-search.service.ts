import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, catchError, map, shareReplay } from 'rxjs';
import { HelpSearchIndex } from './help.types';
import { PreparedDoc, prepare } from './help-search.ranking';

/**
 * Fetches the help search index (public/help/search-index.json), built at prebuild time by
 * scripts/gen-help-manifest.mjs from the same files the "?" drawer renders.
 *
 * Client-side by construction: the corpus is ~120 authored files that only change at deploy time, so
 * an index shipped beside the content is always exactly as fresh as the content. No backend, no API
 * key, no per-query cost — and every result is a real authored page, never a generated sentence.
 *
 * The index (~326 KB, ~80 KB gzipped) is fetched lazily on first use and shared for the session, so a
 * user who never opens search never pays for it. Ranking lives in help-search.ranking.ts.
 */
@Injectable({ providedIn: 'root' })
export class HelpSearchService {
  private readonly http = inject(HttpClient);

  private prepared$?: Observable<readonly PreparedDoc[]>;

  /** Fetch-once, share-forever. Fails closed to an empty corpus: search finds nothing, nothing breaks. */
  load(): Observable<readonly PreparedDoc[]> {
    this.prepared$ ??= this.http.get<HelpSearchIndex>('/help/search-index.json').pipe(
      map((index) => prepare(index.docs ?? [])),
      catchError(() => of([] as readonly PreparedDoc[])),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    return this.prepared$;
  }
}
