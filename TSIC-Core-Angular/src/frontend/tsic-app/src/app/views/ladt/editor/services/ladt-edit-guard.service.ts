import { Injectable } from '@angular/core';

/**
 * Lets the LADT fly-in shell ask the currently-mounted detail panel whether it
 * has unsaved edits, so closing the fly-in or navigating to a sibling can confirm
 * before discarding. Provided at the editor-component level (one per fly-in), so
 * only the panel that's actually on screen registers its probe.
 */
@Injectable()
export class LadtEditGuardService {
  private probe: (() => boolean) | null = null;

  /** The active detail panel registers a closure that reports its live dirty state. */
  register(probe: () => boolean): void {
    this.probe = probe;
  }

  /** Clear on panel destroy — only clears if it's still the registered probe. */
  unregister(probe: () => boolean): void {
    if (this.probe === probe) this.probe = null;
  }

  /** True when the active detail panel reports unsaved changes. */
  isDirty(): boolean {
    return this.probe?.() ?? false;
  }
}
