import type { CanDeactivateFn } from '@angular/router';

/** Implement this interface on components that need unsaved-changes protection. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Route guard that warns users before navigating away from a page with unsaved changes.
 * Uses the browser's native confirm dialog (synchronous — required by CanDeactivate).
 *
 * Usage in routes:
 *   { path: 'job-config', canDeactivate: [unsavedChangesGuard], loadComponent: ... }
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) => {
  if (component.hasUnsavedChanges?.()) {
    return window.confirm('You have unsaved changes. Leave this page?');
  }
  return true;
};
