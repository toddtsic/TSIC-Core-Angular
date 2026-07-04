import { Injectable, inject, signal } from '@angular/core';
import { HelpService } from './help.service';

/**
 * Loads the help manifest once (which pages actually have content) so the "?" launcher can hide
 * itself where there's nothing to show. `canEdit` (server-authoritative, sandbox only) keeps the "?"
 * visible for a SuperUser on unwritten pages — the author-in-place on-ramp.
 */
@Injectable({ providedIn: 'root' })
export class HelpManifestService {
  private readonly help = inject(HelpService);

  private readonly keys = signal<ReadonlySet<string>>(new Set<string>());
  readonly canEdit = signal(false);
  readonly loaded = signal(false);

  constructor() {
    this.reload();
  }

  reload(): void {
    this.help.getManifest().subscribe({
      next: (m) => {
        this.keys.set(new Set(m.keys));
        this.canEdit.set(m.canEdit);
        this.loaded.set(true);
      },
      // Fail closed: no keys → the "?" stays hidden for regular users rather than opening to nothing.
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
