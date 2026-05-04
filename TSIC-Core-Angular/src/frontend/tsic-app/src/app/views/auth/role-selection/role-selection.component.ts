import { ChangeDetectionStrategy, Component, OnInit, inject, computed, signal, ViewChildren, AfterViewInit, QueryList, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { MenuStateService } from '../../../layouts/services/menu-state.service';
import { DropDownListModule, FilteringEventArgs, ChangeEventArgs, FieldSettingsModel, DropDownListComponent } from '@syncfusion/ej2-angular-dropdowns';
import { Query } from '@syncfusion/ej2-data';
import { SuggestedEventsModalComponent } from './suggested-events-modal.component';
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

  /** Below this row count, render a section as a stack of clickable cards instead of a typeahead. */
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
   * True iff the account holds at least one Player registration. Per privilege-separation
   * policy, Family-class and Admin-class accounts are mutually exclusive — so this
   * is a binary account-class signal, not a "could be either" overlap check.
   */
  readonly hasFamilyRegistration = computed(() =>
    this.registrations().some(g => g.roleName === 'Player')
  );

  useTypeahead(roleGroup: { roleRegistrations: unknown[] }): boolean {
    return roleGroup.roleRegistrations.length >= RoleSelectionComponent.TYPEAHEAD_THRESHOLD;
  }

  /**
   * Split the colon-mashed displayText into a title + detail line for cards mode.
   * Player rows look like "JobName:FirstName LastName:AgegroupName:TeamName"; admin
   * rows are usually just "JobName". First segment becomes the title; the rest are
   * joined as a muted detail line.
   */
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
  private _openedOnce = false;

  ngAfterViewInit(): void {
    this.tryOpenFirstDropdown();
    this.dropdowns.changes.subscribe(() => this.tryOpenFirstDropdown());
  }

  private tryOpenFirstDropdown(): void {
    if (this._openedOnce) return;
    // Skip auto-open on mobile — Syncfusion opens a full-screen overlay on touch devices
    if (window.innerWidth < 768) return;
    // Only auto-open when there's exactly ONE role section AND it's the typeahead
    // variant — auto-opening one of multiple sections is presumptuous, and there's
    // nothing to "open" in a cards-mode section (entries are already visible).
    const groups = this.registrations();
    if (groups.length !== 1 || !this.useTypeahead(groups[0])) return;
    const first = this.dropdowns?.first;
    if (first) {
      this._openedOnce = true;
      setTimeout(() => { try { first.showPopup(); } catch { /* no-op */ } }, 0);
    }
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
