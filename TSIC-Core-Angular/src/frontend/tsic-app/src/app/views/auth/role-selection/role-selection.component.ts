import { ChangeDetectionStrategy, Component, OnInit, inject, computed, signal, ViewChildren, AfterViewInit, QueryList, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';

import { NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { MenuStateService } from '../../../layouts/services/menu-state.service';
import { LastLocationService } from '@infrastructure/services/last-location.service';
import { DropDownListModule, FilteringEventArgs, ChangeEventArgs, FieldSettingsModel, DropDownListComponent } from '@syncfusion/ej2-angular-dropdowns';
import { Query } from '@syncfusion/ej2-data';
import { SuggestedEventsModalComponent } from './suggested-events-modal.component';
import { displayRoleName } from '@infrastructure/constants/roles.constants';
import type { RegistrationDto, RegistrationRoleDto } from '@core/api';

/** Two-bucket grouping. Named so ej2's alphabetical group order puts current first. */
type RoleSection = 'Current & upcoming' | 'Past';

/** One picker row: the API registration plus everything the two renderings need pre-derived. */
export interface RoleRow extends RegistrationDto {
  title: string;
  /** The colon-mashed tail (player name, age group, team) — never the date/count, which follow. */
  detail: string;
  dateLabel: string;
  teamLabel: string | null;
  section: RoleSection;
  isPast: boolean;
  /** This row's job is the one whose page the user arrived from — flagged, never auto-opened. */
  isHere: boolean;
}

interface RoleGroupView {
  roleName: string;
  isClubRep: boolean;
  /** Every row, ordered: arrived-at first, then current/upcoming by start, then past by end desc. */
  all: RoleRow[];
  current: RoleRow[];
  past: RoleRow[];
}

/** "Nov 14–15, 2026" / "Nov 14 – Dec 2, 2026" / "Nov 14, 2026" / "". */
export function formatEventDates(start: string | null | undefined, end: string | null | undefined): string {
  const s = start ? new Date(start) : null;
  const e = end ? new Date(end) : null;
  if (!s && !e) return '';
  const mon = (d: Date) => d.toLocaleDateString('en-US', { month: 'short' });
  const one = (d: Date) => `${mon(d)} ${d.getDate()}, ${d.getFullYear()}`;
  if (!s || !e || s.toDateString() === e.toDateString()) return one((s ?? e)!);
  if (s.getFullYear() === e.getFullYear()) {
    return s.getMonth() === e.getMonth()
      ? `${mon(s)} ${s.getDate()}–${e.getDate()}, ${s.getFullYear()}`
      : `${mon(s)} ${s.getDate()} – ${mon(e)} ${e.getDate()}, ${s.getFullYear()}`;
  }
  return `${one(s)} – ${one(e)}`;
}

@Component({
  selector: 'app-role-selection',
  standalone: true,
  imports: [DropDownListModule, SuggestedEventsModalComponent, NgTemplateOutlet],
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
  private readonly lastLocation = inject(LastLocationService);

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

  /** Friendly group header for a role (e.g. ApiAuthorized → "3rd Party Access"). Display only. */
  roleLabel(roleName: string): string {
    return displayRoleName(roleName);
  }

  /**
   * The jobPath whose page the user arrived from. Rows for that job are flagged "this event"
   * and sorted first — pre-selected in the reader's eye, never opened for them: a Director
   * who also holds a Club Rep row here must still choose.
   *
   * The route's own :jobPath is usually the house `tsic` (login redirects here without a
   * job), so the real answer is the last CONFIRMED job the browser was on — the same memory
   * the anonymous landing uses to send people back to their event.
   */
  private readonly arrivedJobPath = computed(() => {
    const fromRoute = (this.route.snapshot.paramMap.get('jobPath')
      ?? this.route.parent?.snapshot.paramMap.get('jobPath')
      ?? '').toLowerCase();
    if (fromRoute && fromRoute !== 'tsic') return fromRoute;
    return (this.lastLocation.getLastJobPath() ?? '').toLowerCase();
  });

  /**
   * Split the colon-mashed displayText into title + detail. Player rows look like
   * "JobName:FirstName LastName:AgegroupName:TeamName"; admin rows are just "JobName".
   * Then date the row from the event window and, for a Club Rep, count its teams.
   */
  private toRow(reg: RegistrationDto, isClubRep: boolean, today: Date): RoleRow {
    const parts = (reg.displayText ?? '').split(':');
    const title = parts[0]?.trim() ?? '';
    const detail = parts.slice(1).map(p => p.trim()).filter(Boolean).join(' • ');
    const endRaw = reg.eventEndDate ?? reg.eventStartDate;
    const end = endRaw ? new Date(endRaw) : null;
    if (end) end.setHours(23, 59, 59, 999);
    const isPast = !!end && end < today;
    const n = reg.teamCount;
    return {
      ...reg,
      title,
      detail,
      dateLabel: formatEventDates(reg.eventStartDate, reg.eventEndDate),
      teamLabel: isClubRep && n !== null && n !== undefined
        ? (n === 0 ? 'no teams yet' : `${n} ${n === 1 ? 'team' : 'teams'}`)
        : null,
      section: isPast ? 'Past' : 'Current & upcoming',
      isPast,
      isHere: !!reg.jobPath && reg.jobPath.toLowerCase() === this.arrivedJobPath(),
    };
  }

  /** The API groups, re-shaped for both renderings. */
  readonly groups = computed<RoleGroupView[]>(() => {
    const today = new Date();
    const time = (v: string | null | undefined) => (v ? new Date(v).getTime() : 0);
    return this.registrations().map((g: RegistrationRoleDto) => {
      const isClubRep = g.roleName === 'Club Rep';
      const rows = g.roleRegistrations.map(r => this.toRow(r, isClubRep, today));
      const current = rows.filter(r => !r.isPast).sort((a, b) =>
        Number(b.isHere) - Number(a.isHere)
        || time(a.eventStartDate) - time(b.eventStartDate)
        || a.title.localeCompare(b.title));
      const past = rows.filter(r => r.isPast).sort((a, b) =>
        Number(b.isHere) - Number(a.isHere)
        || time(b.eventEndDate ?? b.eventStartDate) - time(a.eventEndDate ?? a.eventStartDate)
        || a.title.localeCompare(b.title));
      return { roleName: g.roleName, isClubRep, all: [...current, ...past], current, past };
    });
  });

  /** Local UI signal for selection in progress */
  readonly selectingRole = signal(false);

  /** groupBy renders "Current & upcoming" above "Past" (ej2 orders groups alphabetically — named for it). */
  public fields: FieldSettingsModel = { text: 'displayText', value: 'regId', groupBy: 'section' };

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

    const directorIndex = this.groups().findIndex(g => g.roleName === 'Director');
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

  public onFiltering(e: FilteringEventArgs, group: RoleGroupView): void {
    const text = (e.text ?? '').trim();
    const query = text ? new Query().where('displayText', 'contains', text, true) : new Query();
    e.updateData(group.all as unknown as { [key: string]: object }[], query);
  }

  /**
   * The Club Rep item's second door in typeahead mode. An ej2 list item selects on click, so
   * the Library button must (1) keep the input focused on mousedown - a blur closes the popup
   * before click fires - and (2) stop the click reaching the list, or ej2 selects the row and
   * navigates to the job home instead.
   */
  holdDropdown(e: Event): void {
    e.preventDefault();
    e.stopPropagation();
  }

  openLibraryFromItem(e: Event, row: RoleRow): void {
    e.preventDefault();
    e.stopPropagation();
    this.selectRole(row, 'library');
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

  /**
   * @param destination 'home' lands on the job page (or the returnUrl); 'library' is the
   *   Club Rep row's second door — same registration selected, then straight to the
   *   Club Team Library for that event. A returnUrl still wins: it was asked for first.
   */
  selectRole(registration: { regId: string }, destination: 'home' | 'library' = 'home'): void {
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
          this.router.navigateByUrl(destination === 'library' ? `${routePath}/club/library` : routePath);
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
