import { Injectable, inject, signal } from '@angular/core';
import { environment } from '@environments/environment';
import { HelpService } from './help.service';
import { HelpFsService } from './help-fs.service';

/**
 * Loads the help manifest once (which pages actually have content) so the "?" launcher can hide itself
 * where there's nothing to show. Content is a static asset now, so `canEdit` is a purely local concern:
 * only in development do the served public/help files map to the working tree, so only there can a
 * SuperUser's save persist. On staging/prod there's nothing to write to, so the pencil never appears.
 */
@Injectable({ providedIn: 'root' })
export class HelpManifestService {
  private readonly help = inject(HelpService);
  private readonly fs = inject(HelpFsService);

  private readonly keys = signal<ReadonlySet<string>>(new Set<string>());
  readonly loaded = signal(false);

  /** Editing is a local-dev affordance only, and only where the browser can write files. */
  readonly canEdit = signal(environment.envName === 'development' && this.fs.supported);

  constructor() {
    this.reload();
  }

  reload(): void {
    this.help.getManifest().subscribe({
      next: (m) => {
        this.keys.set(new Set(m.keys));
        this.loaded.set(true);
      },
      // Fail closed: no keys → the "?" stays hidden rather than opening to nothing.
      error: () => this.loaded.set(true),
    });
  }

  has(key: string): boolean {
    return this.keys().has(key);
  }

  /** True when the component has content under any topic (e.g. Help or FAQ) — drives "?" visibility. */
  hasComponent(component: string): boolean {
    const prefix = `${component}/`;
    for (const key of this.keys()) {
      if (key.startsWith(prefix)) return true;
    }
    return false;
  }

  /** After a SuperUser authors a page, remember its key so the "?" persists without a full reload. */
  markAvailable(key: string): void {
    if (this.keys().has(key)) return;
    this.keys.set(new Set([...this.keys(), key]));
  }
}
