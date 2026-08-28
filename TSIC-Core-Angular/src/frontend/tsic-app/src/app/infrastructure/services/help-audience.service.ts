import { Injectable, inject } from '@angular/core';
import { Router, Route } from '@angular/router';
import { AuthService } from './auth.service';
import type { RoleName } from '../constants/roles.constants';

/**
 * Who is allowed to SEE a help topic listed.
 *
 * The route table is the single source of truth: a help topic is reachable exactly when the route
 * that declares its `helpKey` is reachable, and that route already states its own audience in
 * `data.roles` — the same field authGuard enforces. So this walks the live `Router.config` rather
 * than keeping a second list that would drift from it.
 *
 * Walking the real config (not parsing the source) matters because roles INHERIT: `scheduling`
 * declares `roles: [Superuser, Director, SuperDirector]` on the parent and its seven children
 * (fields, pairings, timeslots, …) declare none. Read per-route-object, those children look public.
 * The tree walk carries the ancestor's roles down, which is what the guard does at runtime.
 *
 * SCOPE — this is a visibility filter, not an access control. It decides what appears in the help
 * browse list and search results. The corpus itself ships as a public static asset
 * (public/help/search-index.json), so it is fetchable by anyone who knows the URL regardless of what
 * this hides. Treat it as "don't advertise admin screens to families", not as "families cannot read
 * admin help". Making the latter true needs the admin half served from an authenticated endpoint.
 */
@Injectable({ providedIn: 'root' })
export class HelpAudienceService {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  /** component → roles that may reach it. `null` means the route declares no role restriction. */
  private readonly rolesByComponent = new Map<string, readonly RoleName[] | null>();
  private built = false;

  /**
   * True when the current user could actually navigate to the screen this topic documents.
   * Reads the auth signal, so a computed built on this recomputes when the user's role changes.
   *
   * Fails CLOSED: a component with no route in the table is hidden. `verify:help` already proves
   * every help folder is referenced by a route, so this only fires on genuine drift — and drift
   * should hide a topic, not expose it.
   */
  canSee(component: string): boolean {
    this.build();

    if (!this.rolesByComponent.has(component)) return false;
    const allowed = this.rolesByComponent.get(component);
    if (allowed === null) return true;

    const user = this.auth.currentUser();
    const userRoles: readonly string[] = user?.roles ?? (user?.role ? [user.role] : []);
    return allowed!.some((r) => userRoles.includes(r));
  }

  private build(): void {
    if (this.built) return;
    this.built = true;
    this.walk(this.router.config, null);
  }

  private walk(routes: readonly Route[], inherited: readonly RoleName[] | null): void {
    for (const route of routes) {
      const own = route.data?.['roles'] as RoleName[] | undefined;
      // A route's own roles override the ancestor's; otherwise the ancestor's restriction stands.
      const effective = own && own.length > 0 ? own : inherited;

      const helpKey = route.data?.['helpKey'] as string | undefined;
      if (helpKey) this.record(helpKey.split('/')[0], effective);

      if (route.children) this.walk(route.children, effective);
    }
  }

  /**
   * A component can be documented by more than one route. Take the UNION of what those routes
   * allow — if any reachable route is unrestricted, the topic is unrestricted.
   */
  private record(component: string, roles: readonly RoleName[] | null): void {
    if (!this.rolesByComponent.has(component)) {
      this.rolesByComponent.set(component, roles);
      return;
    }

    const existing = this.rolesByComponent.get(component);
    if (existing === null || roles === null) {
      this.rolesByComponent.set(component, null);
      return;
    }

    this.rolesByComponent.set(component, [...new Set([...existing!, ...roles])]);
  }
}
