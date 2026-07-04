import { Injectable, inject, signal } from '@angular/core';
import { Router, NavigationEnd, ActivatedRouteSnapshot } from '@angular/router';
import { filter } from 'rxjs';

export interface HelpKeyParts {
  component: string;
  topic: string;
}

/**
 * Tracks the help key for the current route. Each route (or route group) declares
 * `data: { helpKey: 'registration-wizard' }`; the deepest activated route wins, so a child can
 * override a parent's default. The single "?" launcher reads this to know what page it's on.
 *
 * Route data does NOT inherit downward (paramsInheritanceStrategy is 'emptyOnly'), so we walk the
 * activated snapshot to the leaf and take the last-defined helpKey.
 */
@Injectable({ providedIn: 'root' })
export class HelpContextService {
  private readonly router = inject(Router);

  /** Raw helpKey from the deepest activated route's data, or null when the page declares none. */
  readonly helpKey = signal<string | null>(this.resolveHelpKey());

  constructor() {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(() => this.helpKey.set(this.resolveHelpKey()));
  }

  /** "component/topic" → parts; a bare "component" defaults the topic to "overview". */
  parseKey(key: string): HelpKeyParts {
    const [component, topic] = key.split('/');
    return { component, topic: topic || 'overview' };
  }

  private resolveHelpKey(): string | null {
    let route: ActivatedRouteSnapshot | null = this.router.routerState.snapshot.root;
    let key: string | null = null;
    while (route) {
      const dataKey = route.data?.['helpKey'];
      if (typeof dataKey === 'string' && dataKey.length > 0) key = dataKey;
      route = route.firstChild;
    }
    return key;
  }
}
