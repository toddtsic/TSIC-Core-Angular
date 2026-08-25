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
    this.tryPrimeDirectorDropdown();
    this.dropdowns.changes.subscribe(() => this.tryPrimeDirectorDropdown());
  }

  /**
   * Land the user in the Director typeahead with the list already open and the
   * filter box focused, so login → type → Enter needs no mouse.
   *
   * **Target**: the `Director` group if the account has one, else the LAST group.
   * `last` alone is wrong for accounts that also hold Player/Staff rows — those
   * groups sort *after* Director (see `RoleLookupService`), so the priming landed
   * on a Player list. In typeahead mode every group renders exactly one
   * `ejs-dropdownlist`, so group index and `QueryList` index line up.
   *
   * **Action**: `focusIn()` then `showPopup()` — the same pair, in the same order,
   * that ej2's own `dropDownClick` runs. Opening is what produces a typing field
   * at all: the filter input lives *inside* the popup, and ej2's popup `open`
   * handler calls `filterInput.focus()`. `focusIn()` on its own (`eea6f5aa`) only
   * highlights the closed control's border — which is why it read as "not working".
   *
   * This re-opens the popup for multi-group accounts, reversing `21023d1b`. Todd
   * asked for it explicitly on 2026-08-25, knowing the popup covers the sections
   * below it until dismissed.
   *
   * Cards mode is never primed — those entries are already on screen. Mobile is
   * skipped entirely: Syncfusion opens a full-screen overlay on touch devices.
   */
  private tryPrimeDirectorDropdown(): void {
    if (this._primedOnce) return;
    if (window.innerWidth < 768) return;
    if (!this.useTypeaheadMode()) return;

    const ddls = this.dropdowns?.toArray() ?? [];
    if (ddls.length === 0) return;

    const directorIndex = this.registrations().findIndex(g => g.roleName === 'Director');
    const index = directorIndex >= 0 && directorIndex < ddls.length ? directorIndex : ddls.length - 1;
    const target = ddls[index];

    this._primedOnce = true;
    setTimeout(() => {
      try {
        target.focusIn();
        target.showPopup();
      } catch (err) {
        // Never silent: a swallowed failure here is indistinguishable from "the
        // feature was never wired up", and that cost a round trip already.
        console.warn('[role-selection] typeahead priming failed', err);
      }
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
