import { ChangeDetectionStrategy, Component, OnInit, inject, computed, signal, ViewChildren, AfterViewInit, QueryList, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { MenuStateService } from '../../../layouts/services/menu-state.service';
import { DropDownListModule, FilteringEventArgs, ChangeEventArgs, FieldSettingsModel, DropDownListComponent } from '@syncfusion/ej2-angular-dropdowns';
import { Query } from '@syncfusion/ej2-data';
import { SuggestedEventsModalComponent } from './suggested-events-modal.component';
import { displayRoleName } from '@infrastructure/constants/roles.constants';
@Component({
  selector: 'app-role-selection',
  standalone: true,
  imports: [DropDownListModule, SuggestedEventsModalComponent],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  templateUrl: './role-selection.component.html',
  styleUrls: ['./role-selection.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RoleSelectionComponent implements OnInit, AfterViewInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly menuState = inject(MenuStateService);

  /** At or above this row count in ANY role group, the whole page renders as typeaheads. */
  private static readonly TYPEAHEAD_THRESHOLD = 7;

  readonly registrations = computed(() => this.authService.registrations());
  readonly suggestedEvents = computed(() => this.authService.suggestedEvents());
  readonly hasSuggestedEvents = computed(() => this.suggestedEvents().length > 0);
  readonly suggestedEventsModalOpen = signal(false);
  readonly isLoading = computed(() => this.authService.registrationsLoading() || this.selectingRole());
  readonly errorMessage = computed(() => this.authService.registrationsError() ?? this.authService.selectError());
  readonly username = computed(() => this.authService.currentUser()?.username ?? '');
  readonly noRegistrationsAvailable = computed(() =>
    !this.isLoading()
    && !this.errorMessage()
    && !this.authService.registrationsLoading()
    && this.registrations().length === 0
  );
  /**
   * True iff the account holds at least one registration in a class that the
   * "suggested events" pivot serves — Family (Player) or ClubRep. Backend
   * decides which audience to query based on registration history; this gate
   * just decides whether to show the pivot link at all.
   */
  readonly hasSuggestableRegistration = computed(() =>
    this.registrations().some(g => g.roleName === 'Player' || g.roleName === 'Club Rep')
  );

  /**
   * Page-wide mode switch: if ANY role group is at/over the threshold, EVERY
   * group renders as a typeahead; otherwise every group renders as cards.
   * Never mix the two controls on one page — a dropdown beside a card list
   * reads as two different UIs for the same task.
   */
  readonly useTypeaheadMode = computed(() =>
    this.registrations().some(g =>
      g.roleRegistrations.length >= RoleSelectionComponent.TYPEAHEAD_THRESHOLD)
  );

  /**
   * Split the colon-mashed displayText into a title + detail line for cards mode.
   * Player rows look like "JobName:FirstName LastName:AgegroupName:TeamName"; admin
   * rows are usually just "JobName". First segment becomes the title; the rest are
   * joined as a muted detail line.
   */
  /** Friendly group header for a role (e.g. ApiAuthorized → "3rd Party Access"). Display only. */
  roleLabel(roleName: string): string {
    return displayRoleName(roleName);
  }

  parseRowParts(displayText: string): { title: string; detail: string } {
    const parts = (displayText ?? '').split(':');
    const title = parts[0]?.trim() ?? '';
    const detail = parts.slice(1).map(p => p.trim()).filter(Boolean).join(' • ');
    return { title, detail };
  }

  /** Local UI signal for selection in progress */
  readonly selectingRole = signal(false);

  public fields: FieldSettingsModel = { text: 'displayText', value: 'regId' };

  /** Optional returnUrl from query params — honored after role selection (e.g. store flow) */
  private _returnUrl: string | null = null;

  ngOnInit(): void {
    // Back-button can reach role-selection after the session was cleared.
    // If not authenticated, redirect to job home (matches cold-start guard).
    if (!this.authService.isAuthenticated()) {
      const jobPath = this.route.snapshot.paramMap.get('jobPath')
        ?? this.route.parent?.snapshot.paramMap.get('jobPath')
        ?? 'tsic';
      this.authService.logoutLocal();
      this.router.navigate([`/${jobPath}`]);
      return;
    }

    const raw = this.route.snapshot.queryParamMap.get('returnUrl');
    // Reject circular returnUrl that points back to role-selection
    this._returnUrl = raw && !raw.includes('role-selection') ? raw : null;
    // Trigger fetch
    this.authService.loadAvailableRegistrations();
    this.authService.loadSuggestedEvents();
  }

  @ViewChildren(DropDownListComponent) readonly dropdowns!: QueryList<DropDownListComponent>;
  private _primedOnce = false;

  ngAfterViewInit(): void {
    this.tryPrimeLastDropdown();
    this.dropdowns.changes.subscribe(() => this.tryPrimeLastDropdown());
  }

  /**
   * Land the user on the typeahead they are most likely to use, so they can start
   * typing without hunting for a control first.
   *
   * Two behaviours, because opening and focusing are not the same thing:
   *
   * - **One group** → `showPopup()`. The SuperUser/director "one giant list" case.
   *   There is nothing else on the page for the popup to cover, so showing the list
   *   immediately is pure help.
   * - **More than one group** (e.g. SuperDirector + Director) → `focusIn()` only.
   *   The caret lands in the LAST section — the lower-privileged one, which is the
   *   more common target — and typing filters from there. Focus alone leaves the
   *   other sections visible, which is what `showPopup()` got wrong here: an
   *   auto-opened popup covered the rest of the page before the user had seen it,
   *   and that is why multi-group auto-open was removed in `21023d1b`. Focusing
   *   restores the intent of `dd408b4e` without reintroducing that problem.
   *
   * Cards mode is never primed — those entries are already on screen. Mobile is
   * skipped entirely: Syncfusion opens a full-screen overlay on touch devices, and
   * focus alone would summon the on-screen keyboard over the page.
   */
  private tryPrimeLastDropdown(): void {
    if (this._primedOnce) return;
    if (window.innerWidth < 768) return;
    if (!this.useTypeaheadMode()) return;

    const last = this.dropdowns?.last;
    if (!last) return;

    const single = this.registrations().length === 1;
    this._primedOnce = true;
    setTimeout(() => {
      try {
        if (single) {
          last.showPopup();
        } else {
          last.focusIn();
        }
      } catch { /* no-op */ }
    }, 0);
  }

  public onFiltering(e: FilteringEventArgs, roleGroup: any): void {
    const text = (e.text ?? '').trim();
    const query = text ? new Query().where('displayText', 'contains', text, true) : new Query();
    e.updateData(roleGroup.roleRegistrations, query);
  }

  public onDropdownChange(e: ChangeEventArgs): void {
    if (e.itemData) {
      this.selectRole(e.itemData as any);
    }
  }

  openSuggestedEventsModal(): void {
    this.suggestedEventsModalOpen.set(true);
  }

  closeSuggestedEventsModal(): void {
    this.suggestedEventsModalOpen.set(false);
  }

  selectRole(registration: any): void {
    // Guard with selectingRole directly — not isLoading() — to prevent re-entry
    // when Syncfusion fires spurious change events during dropdown re-enable
    if (this.selectingRole()) {
      return;
    }

    this.selectingRole.set(true);
    this.authService.selectRegistration(registration.regId).subscribe({
      next: () => {
        // Do NOT reset selectingRole here — keep the dropdown disabled.
        // Re-enabling the Syncfusion dropdown triggers another change event,
        // which fires a second selectRole that races with router.navigate.
        // The component will be destroyed by navigation anyway.
        this.menuState.requestCloseAllMenus();
        const user = this.authService.getCurrentUser();
        if (this._returnUrl) {
          this.router.navigateByUrl(this._returnUrl);
        } else if (user?.jobPath) {
          const routePath = user.jobPath.startsWith('/') ? user.jobPath : '/' + user.jobPath;
          this.router.navigateByUrl(routePath);
        } else {
          // No jobPath in token (shouldn't happen) — re-enable UI as fallback
          this.selectingRole.set(false);
        }
      },
      error: () => {
        this.selectingRole.set(false);
      }
    });
  }

  logout(): void {
    this.authService.logout();
  }
}
