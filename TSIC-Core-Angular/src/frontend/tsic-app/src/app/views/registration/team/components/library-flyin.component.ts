import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, HostListener, OnDestroy, computed, input, output, signal, viewChild } from '@angular/core';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { environment } from '@environments/environment';
import { normalizeLop } from '@shared/teams/lop-choices';
import { LevelOfPlayPickerComponent } from './level-of-play-picker.component';
import { EventAgeGroupPickerComponent } from './event-age-group-picker.component';
import { resolveRecommendedAgeGroupId } from './event-age-group.util';
import { ResizablePanelDirective } from '@shared-ui/directives/resizable-panel.directive';

export interface RegisteredInfo {
    // Raw agegroup name (may be "WAITLIST - {agegroup}") — kept for the functional
    // ageGroupIdByName lookup that pre-selects the picker. Display uses the fields below.
    ageGroupName: string;
    // Display name (prefix stripped) + waitlist flag, resolved once on the backend DTO.
    ageGroupDisplayName: string;
    isWaitlisted: boolean;
    levelOfPlay: string;
}

/** Payload emitted by the inline-expand registration flow. */
export interface RegisterRequest {
    team: ClubTeamDto;
    ageGroupId: string;
    levelOfPlay: string;
}

/**
 * A collapsible section of the active library. `title` is count-free — the count
 * renders as a separate pill in the group-header band. Keys: 'registered' /
 * 'unregistered' when canRegister(), else a single 'all' group.
 */
interface LibraryGroup {
    key: string;
    title: string;
    teams: ClubTeamDto[];
}

/**
 * Library fly-in drawer — picker UI for adding library teams to the current
 * event AND for managing the library (edit / archive / delete / restore).
 *
 * Mirrors the search-registrations / search-teams drawer pattern (proven, see
 * search-registrations.component.scss). z-index 1050/1049 — do not raise.
 *
 * Owns no domain state; parent feeds rows + flags and handles all mutations.
 *
 * Architectural note: the empty-empty branch (active=0 AND archived=0) is
 * intentionally NOT rendered here. The empty-empty path is owned by the
 * combined add-and-register modal in the wizard, so the flyin is only ever
 * opened with library>0. The all-archived branch is still reachable when a
 * rep archives every team, so it remains.
 */
@Component({
    selector: 'app-library-flyin',
    standalone: true,
    imports: [LevelOfPlayPickerComponent, EventAgeGroupPickerComponent, ResizablePanelDirective],
    template: `
    <!-- Portaled to document.body in ngAfterViewInit so the panel + backdrop
         escape <main>'s z-index:0 stacking context (which would otherwise
         keep the layout footer visible underneath them). -->
    <div #flyinRoot class="library-flyin-root">
    @if (isOpen()) {
      <div class="library-backdrop" (click)="onClose()"></div>
    }
    <aside class="library-panel" [class.open]="isOpen()" appResizablePanel storageKey="libraryPanelWidth" panelSide="right" role="dialog" aria-modal="true" aria-labelledby="library-flyin-title">

      <!-- ── Header ─────────────────────────────────────────────────── -->
      <!-- Mirrors search/registrations registration-detail-panel header:
           sentence-case title in body color (not eyebrow style), neutral
           elevated surface background, neutral border. The status chip
           below is a problem-flag (partial/none-registered) only — quiet
           when everything's fine. -->
      <div class="panel-header">
        <div class="header-top-row">
          <h3 class="panel-title" id="library-flyin-title">
            Club Team Library
            @if (clubName()) {
              <span class="title-club-badge">{{ clubName() }}</span>
            }
          </h3>
          <div class="header-actions">
            <button type="button" class="btn-close" aria-label="Close library" (click)="onClose()">&times;</button>
          </div>
        </div>

        <div class="header-info">
          <!-- Per-group count pills below now carry Active/Registered/Archived
               counts, so the old header-tags row was redundant and removed.
               Carryover note demoted from danger-red to a quiet muted line, and
               the three how-to tips tucked into a collapsed disclosure so the
               eye lands on the list, not the preamble. -->
          <p class="library-carryover-tip">
            <i class="bi bi-info-circle" aria-hidden="true"></i>
            Library teams carry across every TSIC event — enter a team once, never retype.
          </p>
          <button type="button" class="lib-howto-toggle"
                  [class.is-open]="showHowTo()"
                  [attr.aria-expanded]="showHowTo()"
                  (click)="toggleHowTo()">
            <i class="bi bi-lightbulb howto-lead" aria-hidden="true"></i>
            How this works
            <i class="bi howto-chevron" [class.bi-chevron-right]="!showHowTo()" [class.bi-chevron-down]="showHowTo()" aria-hidden="true"></i>
          </button>
          @if (showHowTo()) {
            <ul class="lib-howto-list">
              <li>Team not in your library? <strong>Add Library Team</strong>, then <strong>Register</strong> it.</li>
              <li>Name changed? <strong>Add Library Team</strong> with the new name.</li>
              <li>No longer used? Open <i class="bi bi-three-dots-vertical" aria-hidden="true"></i> on its row → <strong>Archive</strong>.</li>
            </ul>
          }
        </div>
      </div>

      <!-- ── Body ───────────────────────────────────────────────────── -->
      <div class="panel-body">
        @if (activeTeams().length === 0) {
          <!-- All-archived (or defensive empty) — hero treatment -->
          <div class="lib-empty-hero">
            <div class="empty-eyebrow">
              <span>
                @if (archivedTeams().length > 0) { Library Archived }
                @else { Library Empty }
              </span>
            </div>
            <i class="bi bi-archive-fill empty-icon" aria-hidden="true"></i>
            <strong class="empty-headline">
              @if (archivedTeams().length > 0) { All your teams are archived }
              @else { Your library is empty }
            </strong>
            <span class="empty-desc">
              @if (archivedTeams().length > 0) {
                Restore a team from the Archived section below, or add a brand-new team to your library.
              } @else {
                Add a team to start your club library.
              }
            </span>
            <button type="button" class="btn btn-primary btn-sm empty-cta" (click)="addNew.emit()">
              <i class="bi bi-plus-circle me-1"></i>Add a New Team
            </button>
          </div>
        } @else {
          <!-- State C — populated active library. Add Library Team moved to a
               top action row now that the single "Active Library" card was split
               into one collapsible section card per group. -->
          <div class="lib-body-actions">
            <button type="button" class="btn-add-team"
                    [disabled]="actionInProgress()"
                    (click)="addNew.emit()">
              <i class="bi bi-plus-circle me-1"></i>Add Library Team
            </button>
          </div>

          <!-- One section card per group (Registered / Not Registered, or a
               single 'all' card when registration is closed). The header band
               adopts the sibling .section-title scale so it outranks the rows. -->
          @for (group of activeGroups(); track group.key) {
            <section class="lib-group-card"
                     [class.lib-group-card--registered]="group.key === 'registered'"
                     [class.lib-group-card--unregistered]="group.key === 'unregistered'">
              <button type="button" class="lib-group-header"
                      [attr.aria-expanded]="!isGroupCollapsed(group.key)"
                      (click)="toggleGroup(group.key)">
                <i class="bi lib-group-chevron"
                   [class.bi-chevron-down]="!isGroupCollapsed(group.key)"
                   [class.bi-chevron-right]="isGroupCollapsed(group.key)"
                   aria-hidden="true"></i>
                @if (group.key === 'registered') {
                  <i class="bi bi-check-circle-fill lib-group-icon" aria-hidden="true"></i>
                }
                <span class="lib-group-title">{{ group.title }}</span>
                <span class="lib-group-count">{{ group.teams.length }}</span>
              </button>

              @if (!isGroupCollapsed(group.key)) {
                <ul class="lib-list">
                  @for (team of group.teams; track team.clubTeamId; let idx = $index) {
                    @let registered = registeredInfo(team.clubTeamId);
                    <li class="lib-item" [class.is-expanded]="expandedTeamId() === team.clubTeamId">
                      <div class="lib-item-main">
                        @if (group.key === 'registered') {
                          <span class="lib-item-seq" aria-hidden="true">{{ idx + 1 }}</span>
                        }
                        <div class="lib-item-id">
                          <span class="lib-item-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
                          <span class="lib-item-sub"><span class="lib-sub-label">Grad</span>{{ team.clubTeamGradYear || '—' }}</span>
                        </div>

                        <div class="lib-item-trailing">
                          @if (registered) {
                            <span class="lib-identity">
                              @if (registered.ageGroupName) {
                                <span class="lib-id-pair"><span class="lib-id-label">AG</span><span class="lib-id-value">{{ registered.ageGroupDisplayName }}@if (registered.isWaitlisted) {<span class="wl-badge" tabindex="0" title="Waitlisted under {{ registered.ageGroupDisplayName }} — placed when a roster spot opens">WL</span>}</span></span>
                              }
                              @if (registered.levelOfPlay) {
                                <span class="lib-id-pair"><span class="lib-id-label">LOP</span><span class="lib-id-value">{{ formatLop(registered.levelOfPlay) }}</span></span>
                              }
                            </span>
                            @if (canRegister() && expandedTeamId() !== team.clubTeamId) {
                              <button type="button" class="lib-icon-btn lib-edit-btn"
                                      title="Edit level of play (age group is locked once registered)"
                                      aria-label="Edit level of play"
                                      [disabled]="actionInProgress()"
                                      (click)="toggleRegister(team)">
                                <i class="bi bi-pencil" aria-hidden="true"></i>
                              </button>
                            }
                          } @else if (canRegister()) {
                            @if (expandedTeamId() !== team.clubTeamId) {
                              <button type="button" class="btn-register-cell"
                                      [disabled]="actionInProgress()"
                                      (click)="toggleRegister(team)">
                                <i class="bi bi-trophy-fill" aria-hidden="true"></i>
                                <span>Register</span>
                              </button>
                            }
                          } @else {
                            <span class="lib-closed-pill">
                              <i class="bi bi-lock-fill" aria-hidden="true"></i>
                              Closed
                            </span>
                          }

                          @if (!registered) {
                          <div class="lib-menu-anchor">
                            <button type="button" class="lib-kebab"
                                    [class.is-open]="openMenuTeamId() === team.clubTeamId"
                                    [disabled]="actionInProgress() || expandedTeamId() === team.clubTeamId"
                                    aria-label="Manage team"
                                    aria-haspopup="menu"
                                    [attr.aria-expanded]="openMenuTeamId() === team.clubTeamId"
                                    (click)="toggleMenu($event, team.clubTeamId)">
                              <i class="bi bi-three-dots-vertical" aria-hidden="true"></i>
                            </button>
                            @if (openMenuTeamId() === team.clubTeamId) {
                              @let editLock = editLockReason(team);
                              @let archiveLock = archiveLockReason(team, !!registered);
                              @let deleteLock = deleteLockReason(team, !!registered);
                              <div class="lib-menu" role="menu" (click)="$event.stopPropagation()">
                                <button type="button" class="lib-menu-item" role="menuitem"
                                        [disabled]="!!editLock"
                                        (click)="handleMenuEdit(team)">
                                  <i class="bi bi-pencil lib-menu-icon" aria-hidden="true"></i>
                                  <span class="lib-menu-label">Edit team</span>
                                  @if (editLock) {
                                    <span class="lib-menu-reason">{{ editLock }}</span>
                                  }
                                </button>
                                <button type="button" class="lib-menu-item" role="menuitem"
                                        [disabled]="!!archiveLock"
                                        (click)="handleMenuArchive(team, !!registered)">
                                  <i class="bi bi-box-arrow-in-down lib-menu-icon" aria-hidden="true"></i>
                                  <span class="lib-menu-label">Archive team</span>
                                  @if (archiveLock) {
                                    <span class="lib-menu-reason">{{ archiveLock }}</span>
                                  }
                                </button>
                                <button type="button" class="lib-menu-item lib-menu-item-danger" role="menuitem"
                                        [disabled]="!!deleteLock"
                                        (click)="handleMenuDelete(team, !!registered)">
                                  <i class="bi bi-trash lib-menu-icon" aria-hidden="true"></i>
                                  <span class="lib-menu-label">Delete team</span>
                                  @if (deleteLock) {
                                    <span class="lib-menu-reason">{{ deleteLock }}</span>
                                  }
                                </button>
                              </div>
                            }
                          </div>
                          }
                        </div>
                      </div>

                      @if (expandedTeamId() === team.clubTeamId) {
                        <div class="lib-item-expand">
                          <div class="register-inline">
                            @if (showDevIds) {
                              <div class="dev-id-title">Club Team <span class="dev-id">(<span class="dev-id-guid">{{ team.clubTeamId }}</span>)</span></div>
                            }
                            <div class="register-step">
                              <label class="field-label fw-bold">Event Level of Play</label>
                              <app-level-of-play-picker
                                [selected]="selectedLop()"
                                (selectedChange)="selectedLop.set($event)" />
                            </div>

                            <div class="register-step">
                              <label class="field-label fw-bold">Event Age Group</label>
                              @if (editingExisting()) {
                                <p class="ag-locked-note">
                                  <i class="bi bi-lock-fill" aria-hidden="true"></i>
                                  Age group is fixed once a team is registered — it drives team caps, waitlists, and fees. To move age groups, remove this team and register it again.
                                </p>
                              }
                              <app-event-age-group-picker
                                variant="chip"
                                [ageGroups]="ageGroups()"
                                [gradYear]="team.clubTeamGradYear"
                                [disabled]="actionInProgress() || lopRequired() || editingExisting()"
                                [selected]="selectedAgeGroupId()"
                                (selectedChange)="selectedAgeGroupId.set($event)" />
                            </div>

                            <div class="register-actions">
                              <button type="button" class="btn-register-cancel" (click)="cancelRegister()">Cancel</button>
                              <button type="button" class="btn-register-submit"
                                      [disabled]="!canSubmit()"
                                      (click)="commitRegister(team)">
                                <i class="bi bi-check-lg" aria-hidden="true"></i>
                                {{ editingExisting() ? 'Save Changes' : 'Submit' }}
                              </button>
                            </div>
                          </div>
                        </div>
                      }
                    </li>
                  }
                </ul>
              }
            </section>
          }

          <!-- Archived group — same section-card chrome, muted tone. -->
          @if (archivedTeams().length > 0) {
            <section class="lib-group-card lib-group-card--archived">
              <button type="button" class="lib-group-header" (click)="toggleArchived()"
                      [attr.aria-expanded]="showArchived()">
                <i class="bi lib-group-chevron"
                   [class.bi-chevron-down]="showArchived()"
                   [class.bi-chevron-right]="!showArchived()"
                   aria-hidden="true"></i>
                <i class="bi bi-archive-fill lib-group-icon" aria-hidden="true"></i>
                <span class="lib-group-title">Archived</span>
                <span class="lib-group-count">{{ archivedTeams().length }}</span>
                <span class="lib-group-hint">hidden from registration</span>
              </button>
              @if (showArchived()) {
                <ul class="lib-list">
                  @for (team of archivedTeams(); track team.clubTeamId) {
                    <li class="lib-item is-archived">
                      <div class="lib-item-main">
                        <div class="lib-item-id">
                          <span class="lib-item-name">{{ team.clubTeamName }}</span>
                          <span class="lib-item-sub"><span class="lib-sub-label">Grad</span>{{ team.clubTeamGradYear || '—' }}</span>
                        </div>
                        <div class="lib-item-trailing">
                          <button type="button" class="lib-icon-btn"
                                  title="Restore to active library"
                                  aria-label="Restore to active library"
                                  [disabled]="actionInProgress()"
                                  (click)="restore.emit(team)">
                            <i class="bi bi-arrow-counterclockwise" aria-hidden="true"></i>
                          </button>
                        </div>
                      </div>
                    </li>
                  }
                </ul>
              }
            </section>
          }
        }

        <!-- Dropped group — teams the rep entered for THIS event that a director
             later moved into a "DROPPED" age group. Read-only history (no register /
             edit / kebab), numbered, muted. Rendered outside the active/empty gate so
             it shows even when the active library is empty. -->
        @if (droppedTeams().length > 0) {
          <section class="lib-group-card lib-group-card--dropped">
            <button type="button" class="lib-group-header" (click)="toggleDropped()"
                    [attr.aria-expanded]="showDropped()">
              <i class="bi lib-group-chevron"
                 [class.bi-chevron-down]="showDropped()"
                 [class.bi-chevron-right]="!showDropped()"
                 aria-hidden="true"></i>
              <i class="bi bi-x-circle-fill lib-group-icon" aria-hidden="true"></i>
              <span class="lib-group-title">Dropped</span>
              <span class="lib-group-count">{{ droppedTeams().length }}</span>
              <span class="lib-group-hint">moved out by the event director</span>
            </button>
            @if (showDropped()) {
              <ul class="lib-list">
                @for (team of droppedTeams(); track team.teamId; let idx = $index) {
                  <li class="lib-item is-dropped">
                    <div class="lib-item-main">
                      <span class="lib-item-seq" aria-hidden="true">{{ idx + 1 }}</span>
                      <div class="lib-item-id">
                        <span class="lib-item-name" [attr.title]="team.teamName">{{ team.teamName }}</span>
                        <span class="lib-item-sub"><span class="lib-sub-label">AG</span>{{ team.ageGroupName || '—' }}</span>
                      </div>
                      <div class="lib-item-trailing">
                        @if (team.levelOfPlay) {
                          <span class="lib-identity">
                            <span class="lib-id-pair"><span class="lib-id-label">LOP</span><span class="lib-id-value">{{ formatLop(team.levelOfPlay) }}</span></span>
                          </span>
                        }
                        <span class="lib-dropped-pill">
                          <i class="bi bi-x-circle" aria-hidden="true"></i>
                          Dropped
                        </span>
                      </div>
                    </div>
                  </li>
                }
              </ul>
            }
          </section>
        }
      </div>

      <!-- ── Footer ─────────────────────────────────────────────────── -->
      <!-- Footer warning was dropped — the header status chip carries the
           same signal (and louder, since it's at the top of the panel). -->
      <div class="panel-footer">
        <div class="footer-buttons">
          <button type="button"
                  class="btn-flyin-done"
                  [class.btn-flyin-done-warning]="showNoneRegisteredWarning()"
                  (click)="onClose()">
            {{ doneLabel() }}
          </button>
        </div>
      </div>
    </aside>
    </div>
    `,
    styles: [`
      /* ── Library Fly-In Drawer ──────────────────────────────────────
         Mirrors search-registrations / search-teams. z-index 1050/1049
         matches detail panels — do not raise. */
      .library-backdrop {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.3);
        z-index: 1049;
      }

      .library-panel {
        position: fixed;
        top: 0;
        right: 0;
        bottom: 0;
        width: 560px;
        max-width: 100vw;
        background: var(--bs-body-bg);
        border-left: 1px solid var(--bs-border-color);
        box-shadow: var(--shadow-xl);
        z-index: 1050;
        transform: translateX(100%);
        transition: transform 0.3s ease;
        display: flex;
        flex-direction: column;

        &.open { transform: translateX(0); }
      }

      /* ── Header — matches registration-detail-panel ──────────────── */
      .panel-header {
        padding: var(--space-5);
        border-bottom: 1px solid var(--bs-border-color);
        background: var(--surface-elevated-bg);
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        gap: var(--space-3);

        .header-top-row {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: var(--space-3);
        }

        .panel-title {
          display: inline-flex;
          align-items: baseline;
          gap: var(--space-2);
          margin: 0;
          font-size: var(--font-size-xl);
          font-weight: var(--font-weight-semibold);
          color: var(--bs-body-color);
          white-space: nowrap;
        }

        /* Club name pill alongside the panel title — quiet primary-tinted
           identifier so the rep sees whose library they're editing. */
        .title-club-badge {
          padding: 2px var(--space-3);
          background: color-mix(in srgb, var(--bs-primary) 12%, transparent);
          color: var(--bs-primary);
          border: 1px solid color-mix(in srgb, var(--bs-primary) 30%, transparent);
          border-radius: 999px;
          font-size: var(--font-size-sm);
          font-weight: var(--font-weight-bold);
          letter-spacing: 0;
        }

        .header-actions {
          display: flex;
          align-items: center;
          gap: var(--space-2);
          flex-shrink: 0;
        }

        .btn-close {
          padding: var(--space-2);
          background: transparent;
          border: none;
          font-size: var(--font-size-xl);
          line-height: 1;
          color: var(--brand-text);
          opacity: 0.5;
          cursor: pointer;
          transition: opacity 0.2s ease;
          flex-shrink: 0;

          &:hover { opacity: 1; }

          &:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
            border-radius: var(--radius-sm);
          }
        }
      }

      /* Header CTA — primary action of the panel, mirrors the position of
         "Delete Registration" in search/registrations: top-right of title area,
         immediately before the close X. Outline + filled-tint pill so it reads
         as a real button without competing with the panel title for weight. */
      .btn-add-team {
        display: inline-flex;
        align-items: center;
        padding: var(--space-1) var(--space-3);
        background: color-mix(in srgb, var(--bs-primary) 10%, transparent);
        border: 1.5px solid var(--bs-primary);
        border-radius: var(--radius-sm);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        transition: background-color 0.1s ease, color 0.1s ease;

        &:hover:not(:disabled) {
          background: var(--bs-primary);
          color: var(--neutral-0);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:disabled { opacity: 0.4; cursor: default; }
      }

      /* ── Body container ────────────────────────────────────────────── */
      .panel-body {
        flex: 1;
        overflow-y: auto;
        padding: var(--space-5);
      }

      /* ── Footer ────────────────────────────────────────────────────── */
      .panel-footer {
        flex-shrink: 0;
        padding: var(--space-3);
        border-top: 1px solid color-mix(in srgb, var(--bs-primary) 18%, transparent);
        background: color-mix(in srgb, var(--bs-primary) 4%, var(--bs-body-bg));
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      .footer-buttons {
        display: flex;
        align-items: center;
        gap: var(--space-2);

        .btn-flyin-done { margin-left: auto; }
      }

      .btn-flyin-done {
        padding: var(--space-2) var(--space-4);
        background: var(--bs-primary);
        border: 1px solid var(--bs-primary);
        border-radius: var(--radius-sm);
        color: var(--neutral-0);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        transition: background-color 0.1s ease, filter 0.1s ease;

        &:hover { filter: brightness(1.1); }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        /* When "Close" semantics — soften the button so it doesn't read as
           a confirmation. The action is dismissive, not committing. */
        &.btn-flyin-done-warning {
          background: transparent;
          color: var(--brand-text);
          border-color: var(--border-color);

          &:hover { background: rgba(var(--bs-body-color-rgb), 0.05); filter: none; }
        }
      }

      /* ── Top action row ────────────────────────────────────────────
         Add Library Team now sits above the group cards (the single
         "Active Library" card header that used to hold it is gone). */
      .lib-body-actions {
        display: flex;
        justify-content: flex-end;
        margin-bottom: var(--space-4);
      }

      /* ── Group section card ────────────────────────────────────────
         One bordered, collapsible card per group (Registered / Not
         Registered / Archived). Mirrors search/registrations .contact-zone.
         NO overflow:hidden — the kebab menu must escape the card. */
      .lib-group-card {
        margin: 0 0 var(--space-4);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-lg);
        background: var(--surface-elevated-bg);
      }

      /* Registered = "done": success header band + a thin success rule down
         the left edge so the whole card reads confirmed — no per-row green. */
      .lib-group-card--registered {
        border-color: color-mix(in srgb, var(--bs-success) 35%, var(--bs-border-color));
        box-shadow: inset 3px 0 0 0 var(--bs-success);
      }

      /* Not Registered = "action": primary accent carries the Register CTA. */
      .lib-group-card--unregistered {
        border-color: color-mix(in srgb, var(--bs-primary) 35%, var(--bs-border-color));
        box-shadow: inset 3px 0 0 0 var(--bs-primary);
      }

      .lib-group-card--archived {
        border-color: var(--bs-border-color);
        background: color-mix(in srgb, var(--bs-body-color) 2%, var(--surface-elevated-bg));
      }

      /* Dropped = read-only history: muted like archived, with a danger-tinted
         left rail to distinguish "removed by director" from "self-archived". */
      .lib-group-card--dropped {
        border-color: color-mix(in srgb, var(--bs-danger) 30%, var(--bs-border-color));
        background: color-mix(in srgb, var(--bs-body-color) 2%, var(--surface-elevated-bg));
        box-shadow: inset 3px 0 0 0 color-mix(in srgb, var(--bs-danger) 45%, transparent);
      }
      .lib-group-card--dropped .lib-group-title { color: var(--brand-text-muted); }
      .lib-group-card--dropped .lib-group-icon { color: color-mix(in srgb, var(--bs-danger) 70%, var(--brand-text-muted)); }

      /* Group header band — full-width accordion toggle. Adopts the sibling
         .section-title scale (sm / semibold / uppercase) so it clearly
         outranks the row content beneath it (the old 10px header was the bug).
         Default state expanded. */
      .lib-group-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-3) var(--space-4);
        background: color-mix(in srgb, var(--bs-body-color) 3%, transparent);
        border: none;
        border-bottom: 1px solid var(--bs-border-color);
        border-radius: var(--radius-lg) var(--radius-lg) 0 0;
        cursor: pointer;
        text-align: left;
        transition: background-color 0.1s ease;

        &:hover { background: color-mix(in srgb, var(--bs-body-color) 6%, transparent); }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
      }

      .lib-group-card--registered .lib-group-header {
        background: color-mix(in srgb, var(--bs-success) 8%, transparent);
        &:hover { background: color-mix(in srgb, var(--bs-success) 12%, transparent); }
      }

      .lib-group-card--unregistered .lib-group-header {
        background: color-mix(in srgb, var(--bs-primary) 7%, transparent);
        &:hover { background: color-mix(in srgb, var(--bs-primary) 11%, transparent); }
      }

      .lib-group-chevron {
        font-size: 0.85em;
        color: var(--brand-text-muted);
        flex-shrink: 0;
      }

      .lib-group-icon {
        font-size: var(--font-size-sm);
        flex-shrink: 0;
        color: var(--brand-text-muted);
      }
      .lib-group-card--registered .lib-group-icon { color: var(--bs-success); }

      .lib-group-title {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: var(--brand-text);
      }
      .lib-group-card--registered .lib-group-title { color: var(--bs-success); }
      .lib-group-card--unregistered .lib-group-title { color: var(--bs-primary); }
      .lib-group-card--archived .lib-group-title { color: var(--brand-text-muted); }

      /* Count pill — rounded-full badge beside the title (replaces the "(18)"
         that used to be baked into the label string). */
      .lib-group-count {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 22px;
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-bold);
        font-variant-numeric: tabular-nums;
        background: color-mix(in srgb, var(--bs-body-color) 8%, transparent);
        color: var(--brand-text-muted);
      }
      .lib-group-card--registered .lib-group-count {
        background: color-mix(in srgb, var(--bs-success) 16%, transparent);
        color: var(--bs-success);
      }
      .lib-group-card--unregistered .lib-group-count {
        background: color-mix(in srgb, var(--bs-primary) 16%, transparent);
        color: var(--bs-primary);
      }

      .lib-group-hint {
        margin-left: auto;
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-normal);
        font-style: italic;
        color: var(--brand-text-muted);
        opacity: 0.85;
      }

      /* Stacked block under the title row: stats line, then tips line. */
      .header-info {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      /* Carry-over note — demoted from danger-red to a quiet muted info line;
         it's helpful context, not an alarm. */
      .library-carryover-tip {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        margin: 0;
        text-align: left;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-normal);

        .bi { color: var(--bs-primary); opacity: 0.8; }
      }

      /* "How this works" disclosure — a primary-tinted pill so it reads as a
         clickable help affordance, not body text (Ann: the old quiet toggle was
         too subtle to notice). Collapsed by default so it never crowds the list. */
      .lib-howto-toggle {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        align-self: flex-start;
        padding: var(--space-1) var(--space-3);
        background: rgba(var(--bs-primary-rgb), 0.1);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.35);
        border-radius: var(--radius-full);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        transition: background-color 0.1s ease, border-color 0.1s ease;

        &:hover {
          background: rgba(var(--bs-primary-rgb), 0.18);
          border-color: rgba(var(--bs-primary-rgb), 0.55);
        }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &.is-open {
          background: rgba(var(--bs-primary-rgb), 0.18);
          border-color: rgba(var(--bs-primary-rgb), 0.55);
        }

        .howto-lead { font-size: 0.95em; }
        .howto-chevron { font-size: 0.75em; opacity: 0.8; }
      }

      @media (prefers-reduced-motion: reduce) {
        .lib-howto-toggle { transition: none; }
      }

      .lib-howto-list {
        margin: 0;
        padding-left: var(--space-5);
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        line-height: var(--line-height-normal);

        li { margin-bottom: 2px; }
        strong { color: var(--brand-text); font-weight: var(--font-weight-semibold); }
        .bi { font-size: 0.9em; }
      }

      /* ── Group list ────────────────────────────────────────────────
         Rows render as a flex list (ledger .txn-row model) inside each group
         card — primary label + muted subtitle on the left, trailing identity +
         actions on the right. Replaces the old fixed-layout <table>. */
      .lib-list {
        list-style: none;
        margin: 0;
        padding: 0;
      }

      .lib-item {
        border-bottom: 1px solid color-mix(in srgb, var(--bs-body-color) 6%, transparent);

        &:last-child { border-bottom: none; }
      }

      .lib-item-main {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-2) var(--space-4);
        transition: background-color 0.1s ease;
      }

      .lib-item:not(.is-archived):hover > .lib-item-main {
        background: color-mix(in srgb, var(--bs-body-color) 3%, transparent);
      }

      /* Active expand source — tie the row to its open editor below. */
      .lib-item.is-expanded > .lib-item-main {
        background: color-mix(in srgb, var(--bs-primary) 5%, transparent);
      }

      /* Leading sequence number — Registered section only. Fixed-width tabular
         column so multi-digit counts stay vertically aligned with the names. */
      .lib-item-seq {
        flex-shrink: 0;
        min-width: 1.5rem;
        text-align: right;
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        font-variant-numeric: tabular-nums;
        color: var(--brand-text-muted);
      }

      /* Left: team name + muted Grad subtitle (the grad-year column retired). */
      .lib-item-id {
        display: flex;
        flex-direction: column;
        gap: 1px;
        min-width: 0;
        flex: 1;
      }

      .lib-item-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      .lib-item-sub {
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }

      .lib-sub-label {
        text-transform: uppercase;
        letter-spacing: 0.06em;
        font-weight: var(--font-weight-semibold);
        opacity: 0.7;
        margin-right: var(--space-1);
      }

      /* Right: event identity + actions cluster. */
      .lib-item-trailing {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        flex-shrink: 0;
      }

      /* Event registration identity (AG · LOP) — the row's payload. Labeled
         2xs eyebrow + sm value pairs (replaces the old 9px / 11px detail). */
      .lib-identity {
        display: inline-flex;
        align-items: baseline;
        gap: var(--space-3);
        white-space: nowrap;
      }

      .lib-id-pair {
        display: inline-flex;
        align-items: baseline;
        gap: var(--space-1);
      }

      .lib-id-label {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--brand-text-muted);
        opacity: 0.75;
      }

      .lib-id-value {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
        font-variant-numeric: tabular-nums;
      }

      /* Waitlist marker on the registered identity's age group — same treatment as the
         registered-teams grid, driven by the backend isWaitlisted flag. */
      .wl-badge {
        margin-left: var(--space-1);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        line-height: 1;
        letter-spacing: 0.02em;
        padding: 2px var(--space-1);
        border-radius: var(--radius-sm);
        color: var(--bs-warning-text-emphasis);
        background: rgba(var(--bs-warning-rgb), 0.15);
        border: 1px solid rgba(var(--bs-warning-rgb), 0.4);
        cursor: default;

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      /* Closed-event trailing pill (registration disabled). */
      .lib-closed-pill {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        font-style: italic;
        font-weight: var(--font-weight-medium);
        white-space: nowrap;
      }

      /* Register button — primary-blue CTA, the focus of a Not-Registered row. */
      .btn-register-cell {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 4px var(--space-3);
        background: color-mix(in srgb, var(--bs-primary) 10%, transparent);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 45%, transparent);
        border-radius: var(--radius-sm);
        color: var(--bs-primary);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        white-space: nowrap;
        transition: background-color 0.12s ease, border-color 0.12s ease, transform 0.12s ease;

        &:hover:not(:disabled) {
          background: var(--bs-primary);
          color: var(--neutral-0);
          border-color: var(--bs-primary);
          transform: translateY(-1px);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:disabled { opacity: 0.4; cursor: default; }
      }

      /* Quiet pencil = edit registration (opens the inline expand). Reuses
         .lib-icon-btn; this only tints it primary alongside the kebab. */
      .lib-edit-btn { color: var(--bs-primary); }

      /* Dropped rows — de-emphasized, read-only. Status pill mirrors .lib-closed-pill
         but danger-tinted to read as "removed", not merely "closed". */
      .lib-item.is-dropped .lib-item-main { opacity: 0.85; }
      .lib-item.is-dropped .lib-item-name {
        color: var(--brand-text-muted);
        text-decoration: line-through;
        text-decoration-color: color-mix(in srgb, var(--bs-danger) 50%, transparent);
      }
      .lib-dropped-pill {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        color: color-mix(in srgb, var(--bs-danger) 80%, var(--brand-text-muted));
        font-size: var(--font-size-xs);
        font-style: italic;
        font-weight: var(--font-weight-medium);
        white-space: nowrap;
      }

      /* Archived rows — de-emphasized, italic name. */
      .lib-item.is-archived .lib-item-main { opacity: 0.8; }
      .lib-item.is-archived .lib-item-name {
        color: var(--brand-text-muted);
        font-style: italic;
        font-weight: var(--font-weight-medium);
      }

      /* ── Inline registration expand ──────────────────────────────
         The Register/Edit control "opens" beneath its row with the LOP +
         Age Group pickers. Re-homed from the old colspan table row into the
         list item; .register-inline (below) keeps its own padding. */
      .lib-item-expand {
        padding: 0;
        background: color-mix(in srgb, var(--bs-primary) 4%, transparent);
        border-top: 1px solid color-mix(in srgb, var(--bs-primary) 18%, transparent);
      }

      .register-inline {
        position: relative;
        padding: var(--space-2) var(--space-3);
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      /* Dev-only ClubTeamId readout, pinned top-right of the register expand.
         Mirrors search/registrations .info-section-title + .reg-id formatting.
         Gated to envName === 'development' — never shows in staging/prod. */
      .dev-id-title {
        position: absolute;
        top: var(--space-2);
        right: var(--space-3);
        margin: 0;
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--text-muted);
        text-transform: uppercase;
        letter-spacing: 0.05em;
        pointer-events: none;
      }

      .dev-id-title .dev-id {
        font-size: var(--font-size-2xs);
        font-family: var(--font-family-mono);
        font-weight: var(--font-weight-medium);
        text-transform: none;
        letter-spacing: normal;
        opacity: 0.7;
      }

      .dev-id-title .dev-id-guid {
        user-select: all;
        pointer-events: auto;
      }

      /* A step inside the register expand: a field-label line stacked tightly
         above its chip row. The register-inline gap (space-2) separates the two
         steps; the within-step gap (space-1) keeps each label glued to its own
         chips. */
      .register-step {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
      }

      /* Locked-age-group note — shown only in edit mode (registered team). The
         picker beneath it is disabled; this explains why and points to the
         remove-and-re-register escape hatch. Quiet muted info line, not an alarm. */
      .ag-locked-note {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        margin: 0;
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        line-height: var(--line-height-normal);

        .bi { color: var(--brand-text-muted); opacity: 0.8; font-size: 0.9em; }
      }

      /* The register expand's two pickers render as shared inline controls:
         <app-level-of-play-picker> (LOP pills) and <app-event-age-group-picker
         variant="chip"> (age-group chips) — each owns its own styles. */

      /* Submit/Cancel row — bottom-right terminal action for the picker. Submit
         commits the selected LOP + age group (a new reg, or a change when
         editing); it stays disabled until the required picks are made. */
      .register-actions {
        display: flex;
        justify-content: flex-end;
        align-items: center;
        gap: var(--space-2);
        margin-top: var(--space-1);
      }

      .btn-register-cancel {
        padding: 4px var(--space-2);
        border: none;
        background: none;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        border-radius: var(--radius-sm);
        cursor: pointer;
      }
      .btn-register-cancel:hover { color: var(--brand-text); text-decoration: underline; }
      .btn-register-cancel:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

      .btn-register-submit {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        padding: 6px var(--space-4);
        border: none;
        border-radius: var(--radius-sm);
        background: var(--bs-primary);
        color: var(--neutral-0);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        transition: filter 0.12s ease, opacity 0.12s ease;
      }
      .btn-register-submit:hover:not(:disabled) { filter: brightness(0.94); }
      .btn-register-submit:disabled { opacity: 0.45; cursor: default; }
      .btn-register-submit:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

      .lib-icon-btn {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 30px;
        height: 30px;
        padding: 0;
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        cursor: pointer;
        transition: background-color 0.1s ease, color 0.1s ease, border-color 0.1s ease;

        &:hover:not(:disabled) {
          background: rgba(var(--bs-primary-rgb), 0.08);
          border-color: rgba(var(--bs-primary-rgb), 0.2);
          color: var(--bs-primary);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:disabled { opacity: 0.3; cursor: default; }
      }

      /* ── Manage kebab menu ──────────────────────────────────────
         Replaces the per-row Edit/Archive/Delete icon stack so every
         row has a single icon (visual calm). Menu opens with all three
         lifecycle actions; unavailable ones show their lock reason. */
      .lib-menu-anchor {
        position: relative;
        display: inline-block;
      }

      .lib-kebab {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 30px;
        height: 30px;
        padding: 0;
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--brand-text-muted);
        font-size: var(--font-size-base);
        cursor: pointer;
        transition: background-color 0.1s ease, color 0.1s ease, border-color 0.1s ease;

        &:hover:not(:disabled),
        &.is-open {
          background: color-mix(in srgb, var(--bs-primary) 8%, transparent);
          border-color: color-mix(in srgb, var(--bs-primary) 22%, transparent);
          color: var(--bs-primary);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:disabled { opacity: 0.3; cursor: default; }
      }

      .lib-menu {
        position: absolute;
        right: 0;
        top: calc(100% + 4px);
        min-width: 260px;
        background: var(--bs-body-bg);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        box-shadow: var(--shadow-md);
        padding: 4px;
        z-index: 10;
        display: flex;
        flex-direction: column;
        gap: 2px;
      }

      .lib-menu-item {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2);
        background: transparent;
        border: none;
        border-radius: var(--radius-sm);
        text-align: left;
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        cursor: pointer;
        transition: background-color 0.1s ease;

        &:hover:not(:disabled) {
          background: color-mix(in srgb, var(--bs-primary) 8%, transparent);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:disabled {
          color: var(--brand-text-muted);
          cursor: default;
          opacity: 0.5;

          .lib-menu-label {
            font-style: italic;
            text-decoration: line-through;
            text-decoration-color: color-mix(in srgb, var(--brand-text-muted) 60%, transparent);
            text-decoration-thickness: 1px;
          }
        }
      }

      .lib-menu-icon {
        font-size: var(--font-size-base);
        flex-shrink: 0;
        width: 18px;
        text-align: center;
      }

      .lib-menu-label {
        font-weight: var(--font-weight-medium);
      }

      .lib-menu-reason {
        margin-left: auto;
        font-size: var(--font-size-xs);
        font-style: italic;
        color: var(--brand-text-muted);
        text-align: right;
        white-space: nowrap;
      }

      .lib-menu-item-danger:not(:disabled) {
        color: var(--bs-danger);

        &:hover {
          background: color-mix(in srgb, var(--bs-danger) 10%, transparent);
        }
      }

      /* ── State B (all-archived / library-empty) — hero treatment ─── */
      .lib-empty-hero {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        text-align: center;
        gap: var(--space-2);
        padding: var(--space-8) var(--space-4) var(--space-6);
        color: var(--brand-text-muted);
      }

      .empty-eyebrow {
        display: inline-flex;
        align-items: center;
        gap: var(--space-3);
        font-size: 11px;
        font-weight: var(--font-weight-bold);
        letter-spacing: 0.1em;
        text-transform: uppercase;
        color: var(--brand-text-muted);

        &::before, &::after {
          content: '';
          display: block;
          width: 40px;
          height: 1px;
        }
        &::before { background: linear-gradient(to right, transparent, rgba(var(--bs-primary-rgb), 0.4)); }
        &::after  { background: linear-gradient(to left,  transparent, rgba(var(--bs-primary-rgb), 0.4)); }

        > span {
          display: inline-flex;
          align-items: center;
          gap: var(--space-2);

          &::before, &::after {
            content: '';
            display: inline-block;
            width: 4px;
            height: 4px;
            border-radius: 50%;
            background: rgba(var(--bs-primary-rgb), 0.5);
          }
        }
      }

      .empty-icon {
        font-size: 44px;
        color: rgba(var(--bs-primary-rgb), 0.45);
        margin-top: var(--space-2);
      }

      .empty-headline {
        color: var(--brand-text);
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-semibold);
      }

      .empty-desc {
        max-width: 380px;
        font-size: var(--font-size-sm);
        line-height: var(--line-height-normal);
      }

      .empty-cta {
        margin-top: var(--space-3);
      }

      @media (prefers-reduced-motion: reduce) {
        .library-panel { transition: none; }
        .lib-icon-btn, .btn-register-cell, .btn-add-team, .btn-flyin-done,
        .lib-group-header, .lib-group-card, .lib-howto-toggle,
        .lib-item-main { transition: none; }
        .btn-register-cell:hover:not(:disabled) { transform: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryFlyinComponent implements AfterViewInit, OnDestroy {
    private readonly flyinRoot = viewChild.required<ElementRef<HTMLElement>>('flyinRoot');

    /**
     * The library-flyin lives inside <router-outlet> inside <main>, and <main>
     * has z-index:0 (an explicit stacking context that traps page content from
     * competing with header dropdowns at z-index:10001). That trap also keeps
     * the panel + backdrop from painting over the layout's client-footer-bar
     * which sits as a flex sibling of <main> in the layout-wrapper. Portaling
     * the wrapper element to document.body escapes the stacking context so
     * the panel covers the full viewport, just like bottom-nav and CDK
     * overlays do at the root level.
     */
    ngAfterViewInit(): void {
        const root = this.flyinRoot()?.nativeElement;
        if (root && root.parentElement !== document.body) {
            document.body.appendChild(root);
        }
    }

    ngOnDestroy(): void {
        const root = this.flyinRoot()?.nativeElement;
        if (root?.parentNode) {
            root.parentNode.removeChild(root);
        }
    }

    readonly isOpen = input.required<boolean>();
    readonly clubTeams = input.required<ClubTeamDto[]>();
    /** Teams a director moved into a "DROPPED" age group — read-only history shown
     *  in its own muted section. Never offered for registration. */
    readonly droppedTeams = input<readonly RegisteredTeamDto[]>([]);
    readonly clubName = input<string>('');
    readonly canRegister = input(false);
    /** Director's per-event "Allow Edit" toggle, already folded with the eventConcluded door
     *  (false on a concluded event regardless of the toggle). Gates the "Edit team" menu item. */
    readonly canEdit = input(false);
    readonly actionInProgress = input(false);
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    /** Dev-only diagnostics toggle — surfaces ClubTeamId in the register expand.
     *  True only under the `development` env overlay (local ng serve); never in
     *  staging or production. */
    readonly showDevIds = environment.envName === 'development';
    /** Map of clubTeamId → registration info. Drives the Registered badge content. */
    readonly enteredTeams = input<ReadonlyMap<number, RegisteredInfo>>(new Map());

    readonly closed = output<void>();
    readonly register = output<RegisterRequest>();
    readonly addNew = output<void>();
    readonly edit = output<ClubTeamDto>();
    readonly archive = output<ClubTeamDto>();
    readonly delete = output<ClubTeamDto>();
    readonly restore = output<ClubTeamDto>();

    /** Local UI state for collapsible archived sub-section. Default expanded
     *  so the restore action is reachable without an extra click. */
    readonly showArchived = signal(true);
    toggleArchived(): void { this.showArchived.set(!this.showArchived()); }

    readonly showDropped = signal(true);
    toggleDropped(): void { this.showDropped.set(!this.showDropped()); }

    /** Header "How this works" disclosure — collapsed by default so the tips
     *  don't crowd the list; a returning rep already knows the flow. */
    readonly showHowTo = signal(false);
    toggleHowTo(): void { this.showHowTo.set(!this.showHowTo()); }

    /** Collapsed active groups (keyed by group key — 'registered' / 'unregistered').
     *  Empty = all expanded, the default. Mirrors the Archived collapse affordance
     *  so a rep can fold away whichever group they're done reviewing. */
    readonly collapsedGroups = signal<ReadonlySet<string>>(new Set());
    isGroupCollapsed(key: string): boolean { return this.collapsedGroups().has(key); }
    toggleGroup(key: string): void {
        const next = new Set(this.collapsedGroups());
        if (next.has(key)) next.delete(key); else next.add(key);
        this.collapsedGroups.set(next);
    }

    /** Active row whose kebab menu is open (null = none). */
    readonly openMenuTeamId = signal<number | null>(null);

    toggleMenu(event: MouseEvent, teamId: number): void {
        event.stopPropagation();
        this.openMenuTeamId.set(this.openMenuTeamId() === teamId ? null : teamId);
    }

    closeMenu(): void {
        this.openMenuTeamId.set(null);
    }

    // ── Inline registration expand ─────────────────────────────────────
    // Click "Register" (unregistered row) or "Edit" (registered row) → that row
    // expands beneath itself with a two-step picker: LOP chips, then age-group
    // chips. Both are SELECT-ONLY (highlight); a bottom-right Submit commits.
    // Editing pre-fills both from the current registration. Replaces the old
    // click-an-age-group-to-commit model so fresh + edit flows are consistent.

    readonly expandedTeamId = signal<number | null>(null);
    readonly selectedLop = signal('');
    readonly selectedAgeGroupId = signal('');

    /** True until the rep picks a 1–5 level of play (always required now). */
    readonly lopRequired = computed(() => !this.selectedLop());

    /** Expanded row is an already-registered team → Submit is a change, not a new reg. */
    readonly editingExisting = computed(() => {
        const id = this.expandedTeamId();
        return id !== null && this.enteredTeams().has(id);
    });

    /** Submit is enabled once an age group is picked and any required LOP is set. */
    readonly canSubmit = computed(() =>
        !this.actionInProgress() && !!this.selectedAgeGroupId() && !this.lopRequired(),
    );

    toggleRegister(team: ClubTeamDto): void {
        if (this.expandedTeamId() === team.clubTeamId) {
            this.cancelRegister();
            return;
        }
        // Pre-fill the picker. When editing a registered team, seed both chips
        // from the current registration; otherwise seed LOP from the library
        // team's stored value (if it's a valid option) and pre-select the
        // grad-year best-match age group (the recommended/asterisked chip) so the
        // common case is Submit-ready. Falls back to unselected when there's no
        // match, leaving Submit disabled until the rep chooses.
        const existing = this.registeredInfo(team.clubTeamId);
        const lopCandidate = existing?.levelOfPlay || team.clubTeamLevelOfPlay || '';
        this.selectedLop.set(normalizeLop(lopCandidate));
        this.selectedAgeGroupId.set(
            existing
                ? this.ageGroupIdByName(existing.ageGroupName)
                : resolveRecommendedAgeGroupId(this.ageGroups(), team.clubTeamGradYear),
        );
        this.closeMenu();
        this.expandedTeamId.set(team.clubTeamId);
    }

    cancelRegister(): void {
        this.expandedTeamId.set(null);
        this.selectedAgeGroupId.set('');
    }

    /** Resolve an age group's id from its name (RegisteredInfo carries name only). */
    private ageGroupIdByName(name: string): string {
        if (!name) return '';
        return this.ageGroups().find(a => a.ageGroupName === name)?.ageGroupId ?? '';
    }

    commitRegister(team: ClubTeamDto): void {
        const ageGroupId = this.selectedAgeGroupId();
        if (!ageGroupId || this.lopRequired() || this.actionInProgress()) return;
        this.register.emit({ team, ageGroupId, levelOfPlay: this.selectedLop() });
        this.cancelRegister();
    }

    /** Returns the lock reason for Edit, or null if available. */
    editLockReason(team: ClubTeamDto): string | null {
        if (!this.canEdit()) return 'Editing is off for this event';
        if (team.bHasBeenScheduled) return 'Has event history';
        return null;
    }

    /** Returns the lock reason for Archive, or null if available. */
    archiveLockReason(team: ClubTeamDto, registered: boolean): string | null {
        if (!team.bHasBeenScheduled) return 'Use Delete — no event history';
        if (registered) return 'Registered for this event';
        return null;
    }

    /** Returns the lock reason for Delete, or null if available. */
    deleteLockReason(team: ClubTeamDto, registered: boolean): string | null {
        if (team.bHasBeenScheduled) return 'Use Archive — has event history';
        if (registered) return 'Registered for this event';
        return null;
    }

    handleMenuEdit(team: ClubTeamDto): void {
        this.closeMenu();
        if (!this.editLockReason(team)) this.edit.emit(team);
    }

    handleMenuArchive(team: ClubTeamDto, registered: boolean): void {
        this.closeMenu();
        if (!this.archiveLockReason(team, registered)) this.archive.emit(team);
    }

    handleMenuDelete(team: ClubTeamDto, registered: boolean): void {
        this.closeMenu();
        if (!this.deleteLockReason(team, registered)) this.delete.emit(team);
    }

    @HostListener('document:click')
    onDocumentClick(): void {
        if (this.openMenuTeamId() !== null) this.closeMenu();
    }

    readonly activeTeams = computed(() =>
        this.clubTeams()
            .filter(t => !t.bArchived)
            .sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName)),
    );

    readonly archivedTeams = computed(() =>
        this.clubTeams()
            .filter(t => t.bArchived)
            .sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName)),
    );

    /**
     * Active teams split into Registered (top) then Not Registered, each
     * alphabetical within itself. Registered surfaces first because the common
     * return visit is to confirm/edit the roster already entered for this event;
     * the unregistered group below (plus the header count + none-registered
     * warning) still carries the "what's left" signal. When registration is
     * closed there's nothing to register, so it collapses to one flat group.
     *
     * Alphabetical-within-group keeps spatial memory stable — only the natural
     * group change on register/unregister moves a row (a row jumping to the
     * Registered group reads as "moved to done", which is the intended cue).
     */
    readonly activeGroups = computed<LibraryGroup[]>(() => {
        const all = this.activeTeams();
        if (!this.canRegister()) {
            // Registration closed → grouping by registration is meaningless; show
            // one neutral card with every team in its "Closed" trailing state.
            return [{ key: 'all', title: 'Active Library', teams: all }];
        }
        const entered = this.enteredTeams();
        const registered: ClubTeamDto[] = [];
        const unregistered: ClubTeamDto[] = [];
        for (const t of all) {
            (entered.has(t.clubTeamId) ? registered : unregistered).push(t);
        }
        const groups: LibraryGroup[] = [];
        if (registered.length) {
            groups.push({ key: 'registered', title: 'Registered', teams: registered });
        }
        if (unregistered.length) {
            groups.push({ key: 'unregistered', title: 'Not Registered', teams: unregistered });
        }
        return groups;
    });

    readonly registeredCount = computed(() => {
        const entered = this.enteredTeams();
        let count = 0;
        for (const t of this.activeTeams()) {
            if (entered.has(t.clubTeamId)) count++;
        }
        return count;
    });

    /**
     * Show the no-show-trap warning chip + soften "Done" to "Close" when the
     * rep has library teams in front of them but none registered for the event.
     * This is the silent-failure mitigation — closing without registering walks
     * the rep into a no-show situation Saturday.
     */
    readonly showNoneRegisteredWarning = computed(() =>
        this.activeTeams().length > 0 && this.registeredCount() === 0,
    );

    readonly doneLabel = computed(() => this.showNoneRegisteredWarning() ? 'Close' : 'Done');

    isEntered(clubTeamId: number): boolean {
        return this.enteredTeams().has(clubTeamId);
    }

    registeredInfo(clubTeamId: number): RegisteredInfo | undefined {
        return this.enteredTeams().get(clubTeamId);
    }

    /**
     * LOP display: strip parenthetical/textual modifier from a numbered value.
     * "5 (strongest)" → "5"; "Recreational" → "Recreational" (passthrough).
     */
    formatLop(lop: string | null | undefined): string {
        if (!lop) return '';
        const match = lop.match(/^\s*(\d+)/);
        return match ? match[1] : lop;
    }

    onClose(): void {
        this.closed.emit();
    }

    @HostListener('document:keydown.escape')
    onEscape(): void {
        if (this.openMenuTeamId() !== null) {
            this.closeMenu();
            return;
        }
        if (this.expandedTeamId() !== null) {
            this.cancelRegister();
            return;
        }
        if (this.isOpen()) this.onClose();
    }
}
