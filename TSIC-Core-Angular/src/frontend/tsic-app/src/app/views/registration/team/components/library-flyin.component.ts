import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, HostListener, OnDestroy, computed, input, output, signal, viewChild } from '@angular/core';
import type { AgeGroupDto, ClubTeamDto } from '@core/api';

export interface RegisteredInfo {
    ageGroupName: string;
    levelOfPlay: string;
}

/** Payload emitted by the inline-expand registration flow. */
export interface RegisterRequest {
    team: ClubTeamDto;
    ageGroupId: string;
    levelOfPlay: string;
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
    template: `
    <!-- Portaled to document.body in ngAfterViewInit so the panel + backdrop
         escape <main>'s z-index:0 stacking context (which would otherwise
         keep the layout footer visible underneath them). -->
    <div #flyinRoot class="library-flyin-root">
    @if (isOpen()) {
      <div class="library-backdrop" (click)="onClose()"></div>
    }
    <aside class="library-panel" [class.open]="isOpen()" role="dialog" aria-modal="true" aria-labelledby="library-flyin-title">

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
          <div class="header-tags">
            <span class="header-tag">
              <span class="header-tag-label">Active:</span>
              <span class="header-tag-value">{{ activeTeams().length }}</span>
            </span>
            <span class="header-tag">
              <span class="header-tag-label">Archived:</span>
              <span class="header-tag-value">{{ archivedTeams().length }}</span>
            </span>
            <span class="header-tag">
              <span class="header-tag-label">Registered:</span>
              <span class="header-tag-value">{{ registeredCount() }}</span>
            </span>
          </div>
          <p class="library-carryover-tip">Library teams carry across every TSIC event — enter a team once, never retype.</p>
          <div class="wizard-tip">
            <ul class="mb-0">
              <li>To register a team that isn't in your library, <strong>Add Library Team</strong>, then click <strong>Register</strong> on its row.</li>
              <li>If a team's name has changed, <strong>Add Library Team</strong> and use this one.</li>
              <li>If a team is no longer used, click <i class="bi bi-three-dots-vertical" aria-hidden="true"></i> on its row and choose <strong>Archive</strong>.</li>
            </ul>
          </div>
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
          <!-- State C — populated active list rendered as audit table inside a section card -->
          <section class="lib-section-card">
            <header class="lib-section-card-header">
              <span class="lib-section-card-eyebrow">Active Library</span>
              <button type="button" class="btn-add-team header-add-team"
                      [disabled]="actionInProgress()"
                      (click)="addNew.emit()">
                <i class="bi bi-plus-circle me-1"></i>Add Library Team
              </button>
            </header>
            <table class="lib-table">
              <thead>
                <tr>
                  <th class="lib-th-team">Library Team</th>
                  <th class="lib-th-age">Library Grad Year</th>
                  <th class="lib-th-status">Status</th>
                  <th class="lib-th-actions">
                    <span class="visually-hidden">Manage</span>
                  </th>
                </tr>
              </thead>
            <tbody>
              @for (team of sortedActiveTeams(); track team.clubTeamId) {
                @let registered = registeredInfo(team.clubTeamId);
                <tr class="lib-tr" [class.is-registered]="!!registered">
                  <td class="lib-td-team">
                    <span class="team-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
                  </td>
                  <td class="lib-td-age">{{ team.clubTeamGradYear || '—' }}</td>
                  <td class="lib-td-status">
                    @if (registered) {
                      <div class="status-block status-block-yes">
                        <span class="status-line">
                          <i class="bi bi-check-circle-fill" aria-hidden="true"></i>
                          Registered
                        </span>
                        <span class="status-detail">
                          @if (registered.ageGroupName) {
                            <span class="reg-label">AG</span>
                            <span class="reg-value">{{ registered.ageGroupName }}</span>
                          }
                          @if (registered.ageGroupName && registered.levelOfPlay) {
                            <span class="reg-sep"> · </span>
                          }
                          @if (registered.levelOfPlay) {
                            <span class="reg-label">LOP</span>
                            <span class="reg-value">{{ formatLop(registered.levelOfPlay) }}</span>
                          }
                        </span>
                        @if (canRegister() && expandedTeamId() !== team.clubTeamId) {
                          <button type="button" class="btn-register-cell btn-edit-cell"
                                  [disabled]="actionInProgress()"
                                  (click)="toggleRegister(team)">
                            <i class="bi bi-pencil" aria-hidden="true"></i>
                            <span>Edit</span>
                          </button>
                        }
                      </div>
                    } @else if (canRegister()) {
                      <div class="status-block status-block-no">
                        <span class="status-line">Not Registered</span>
                        @if (expandedTeamId() !== team.clubTeamId) {
                          <button type="button" class="btn-register-cell"
                                  [disabled]="actionInProgress()"
                                  (click)="toggleRegister(team)">
                            <i class="bi bi-trophy-fill" aria-hidden="true"></i>
                            <span>Register</span>
                          </button>
                        }
                      </div>
                    } @else {
                      <div class="status-block status-block-closed">
                        <span class="status-line">
                          <i class="bi bi-lock-fill" aria-hidden="true"></i>
                          Closed
                        </span>
                      </div>
                    }
                  </td>
                  <td class="lib-td-actions">
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
                  </td>
                </tr>

                @if (expandedTeamId() === team.clubTeamId) {
                  <tr class="lib-tr-expand">
                    <td colspan="4" class="lib-td-expand">
                      <div class="register-inline">
                        @if (lopOptions().length > 0) {
                          <div class="register-step">
                            <span class="register-step-label">First, pick a Level of Play:</span>
                            <div class="lop-pills" role="radiogroup" aria-label="Level of play">
                              @for (opt of lopOptions(); track opt) {
                                <button type="button" class="lop-pill"
                                        [class.active]="selectedLop() === opt"
                                        (click)="selectedLop.set(opt)">
                                  {{ opt }}
                                </button>
                              }
                            </div>
                          </div>
                        }

                        <div class="register-step">
                          <span class="register-step-label">
                            @if (lopOptions().length > 0) {
                              Then pick an event age group:
                            } @else {
                              Pick an event age group:
                            }
                          </span>
                          <div class="ag-chip-row">
                          @for (ag of expandedAgChips(); track ag.ageGroupId) {
                            <button type="button" class="ag-chip"
                                    [class.is-recommended]="ag.isRecommended"
                                    [class.is-selected]="selectedAgeGroupId() === ag.ageGroupId"
                                    [class.is-full]="ag.isFull"
                                    [class.is-almost-full]="ag.isAlmostFull"
                                    [disabled]="actionInProgress() || lopRequired()"
                                    [title]="ag.isFull ? 'Age group is full — registering will waitlist this team' : null"
                                    (click)="selectedAgeGroupId.set(ag.ageGroupId)">
                              <span class="ag-chip-name">{{ ag.ageGroupName }}@if (ag.matchesGradYear) {<span class="ag-chip-gradyear-match" title="Matches this team's grad year" aria-label="Matches this team's grad year">*</span>}</span>
                              <span class="ag-chip-meta">
                                @if (ag.isFull) { Waitlist }
                                @else if (ag.isAlmostFull) { {{ ag.spotsLeft }} left }
                              </span>
                            </button>
                          }
                          </div>
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
                    </td>
                  </tr>
                }
              }
            </tbody>
            </table>
          </section>

          <!-- Archived sub-section (collapsible) -->
          @if (archivedTeams().length > 0) {
            <button type="button" class="lib-section-toggle" (click)="toggleArchived()">
              <i class="bi lib-section-toggle-chevron" [class.bi-chevron-right]="!showArchived()" [class.bi-chevron-down]="showArchived()"></i>
              <i class="bi bi-archive-fill"></i>
              <span class="lib-section-toggle-label">Archived ({{ archivedTeams().length }})</span>
              <span class="lib-section-toggle-hint">teams hidden from registration</span>
            </button>
            @if (showArchived()) {
              <section class="lib-section-card lib-section-card--archived">
                <table class="lib-table lib-table-archived">
                  <tbody>
                    @for (team of archivedTeams(); track team.clubTeamId) {
                      <tr class="lib-tr is-archived">
                        <td class="lib-td-team">
                          <span class="team-name">{{ team.clubTeamName }}</span>
                        </td>
                        <td class="lib-td-age">{{ team.clubTeamGradYear || '—' }}</td>
                        <td class="lib-td-status">
                          <span class="status-archived">
                            <i class="bi bi-archive-fill" aria-hidden="true"></i>
                            Archived
                          </span>
                        </td>
                        <td class="lib-td-actions">
                          <button type="button" class="lib-icon-btn"
                                  title="Restore to active library"
                                  [disabled]="actionInProgress()"
                                  (click)="restore.emit(team)">
                            <i class="bi bi-arrow-counterclockwise"></i>
                          </button>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </section>
            }
          }
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

      /* ── Section frame ─────────────────────────────────────────────
         Mirrors search/registrations .contact-zone: solid primary border,
         soft tint background, and an in-card header row (eyebrow on the
         left, metadata pair on the right) followed by a divider. The whole
         card reads as one bordered grouping — eyebrow lives inside, not
         floating above. */
      .lib-section-card {
        margin: 0 0 var(--space-5);
        border: 1px solid var(--bs-primary);
        border-radius: var(--radius-lg);
        background: var(--surface-elevated-bg);
        /* No overflow:hidden — the kebab dropdown menu must escape the card.
           Tradeoff: the table inside has square corners while the card has
           rounded corners. Mismatch is visually minor (subtle bg tints, small
           radius). Clipping the menu would be a real functional bug. */
      }

      .lib-section-card--archived {
        border-color: var(--border-color);
        background: var(--surface-elevated-bg);
      }

      /* In-card header row: eyebrow that names the table within the card.
         Matches search/registrations .section-title token-for-token. */
      .lib-section-card-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-3) var(--space-4);
        border-bottom: 1px solid var(--bs-border-color);
      }

      /* Add Library Team button — flushed right inside the Active Library
         section header (replaces the top-of-flyin position). */
      .lib-section-card-header .header-add-team { margin-left: auto; }

      .lib-section-card-eyebrow {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        letter-spacing: 0.05em;
        text-transform: uppercase;
        color: var(--bs-primary);
      }

      /* Stacked block under the title row: stats line, then tips line. */
      .header-info {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      /* Carry-over headline tip — sits above the bullet list, left-justified,
         in palette danger so it reads as the standout "this library is permanent"
         message rather than just another bullet. */
      .library-carryover-tip {
        margin: 0;
        text-align: left;
        color: var(--bs-danger);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
      }

      /* Header tag pairs — single horizontal line in this flyin so the
         tips column gets more horizontal real estate. Search/teams stays
         stacked. */
      .header-tags {
        display: flex;
        flex-direction: row;
        flex-wrap: wrap;
        align-items: baseline;
        gap: var(--space-1) var(--space-3);
      }


      .header-tag {
        display: inline-flex;
        align-items: baseline;
        gap: var(--space-2);

        .header-tag-label {
          font-size: var(--font-size-2xs);
          font-weight: var(--font-weight-medium);
          color: var(--text-muted);
          text-transform: uppercase;
          letter-spacing: 0.04em;
          opacity: 0.7;
        }

        .header-tag-value {
          font-size: var(--font-size-sm);
          font-weight: var(--font-weight-semibold);
          color: var(--bs-primary);
        }
      }

      /* Section toggle (replaces the standalone archived-header) — same
         eyebrow weight as .lib-section-title but interactive. */
      .lib-section-toggle {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: calc(100% - var(--space-3) * 2);
        margin: 0 var(--space-3) var(--space-2);
        padding: var(--space-1) 0;
        background: transparent;
        border: none;
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.05em;
        cursor: pointer;
        text-align: left;
        transition: color 0.1s ease;

        &:hover { color: var(--brand-text); }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
          border-radius: var(--radius-sm);
        }
      }

      .lib-section-toggle-chevron { font-size: 0.85em; }
      .lib-section-toggle-label { flex-shrink: 0; }
      .lib-section-toggle-hint {
        margin-left: auto;
        text-transform: none;
        letter-spacing: 0;
        font-weight: var(--font-weight-normal);
        font-size: 11px;
        opacity: 0.75;
      }

      /* ── Library audit table ───────────────────────────────────────
         Hand-rolled compact table for the team list. Status column does
         double duty: shows ✓ + age group when registered, Register button
         when not. Actions column carries library-lifecycle (edit / archive
         / delete) — separated from the event-axis Status column. */
      .lib-table {
        width: 100%;
        border-collapse: collapse;
        font-size: var(--font-size-sm);
        table-layout: fixed;
      }

      .lib-table thead th {
        padding: var(--space-2);
        text-align: left;
        vertical-align: bottom;
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        line-height: 1.2;
        color: var(--brand-text-muted);
        background: color-mix(in srgb, var(--bs-primary) 5%, transparent);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-primary) 25%, transparent);
      }

      /* Column widths sized for the 520px panel. */
      .lib-th-team    { width: auto; }
      .lib-th-age     { width: 80px; }
      .lib-th-status  { width: 130px; }
      .lib-table thead th.lib-th-age,
      .lib-table thead th.lib-th-status { text-align: center; }
      /* 76px = MANAGE label (~50px) + 8px left pad + 12px right pad + slack. */
      .lib-th-actions { width: 76px; }

      /* Inset rightmost column from the card border so MANAGE / kebab don't
         crowd the edge after the border was strengthened to solid primary. */
      .lib-table thead th:last-child,
      .lib-table tbody td:last-child {
        padding-right: var(--space-3);
      }

      .lib-tr {
        border-bottom: 1px solid color-mix(in srgb, var(--bs-body-color) 6%, transparent);
        transition: background-color 0.1s ease;

        &:last-child { border-bottom: none; }
        &:hover:not(.is-archived) {
          background: color-mix(in srgb, var(--bs-body-color) 3%, transparent);
        }
      }

      /* Subtle green wash on registered rows — the Status column carries
         most of the signal, but a faint row tint confirms it row-wide. */
      .lib-tr.is-registered {
        background: color-mix(in srgb, var(--bs-success) 5%, transparent);

        &:hover { background: color-mix(in srgb, var(--bs-success) 9%, transparent); }
      }

      .lib-table td {
        padding: var(--space-2);
        vertical-align: middle;
      }

      .lib-td-age,
      .lib-td-status {
        text-align: center;
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }

      .lib-td-team {
        min-width: 0;
      }

      .team-name {
        display: block;
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        min-width: 0;
      }

      /* ── Registration Status column — three states ──────────────────
         Each cell stacks an explicit status label (line 1) above either a
         detail string (registered) or a CTA button (not yet registered).
         Center-aligned so the column reads as a status badge column rather
         than a mixed action/info hybrid. */
      .status-block {
        display: inline-flex;
        flex-direction: column;
        align-items: center;
        gap: 2px;
        white-space: nowrap;
      }

      .status-block .status-line {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }

      .status-block .status-detail {
        font-size: 11px;
        color: var(--brand-text-muted);
      }

      /* Labeled axis values inside the registered status detail.
         "AG"/"LOP" eyebrow distinguishes the registered age group from the
         library team's grad year (which can share the same numeric value
         when tournaments name age groups by grad year). */
      .status-block .status-detail .reg-label {
        font-size: 9px;
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        opacity: 0.75;
        margin-right: 4px;
      }

      .status-block .status-detail .reg-value {
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
      }

      .status-block .status-detail .reg-sep {
        opacity: 0.5;
      }

      .status-block-yes .status-line { color: var(--bs-success); }

      .status-block-no .status-line { color: var(--brand-text-muted); }

      .status-block-closed .status-line {
        color: var(--brand-text-muted);
        font-style: italic;
      }

      .status-archived {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        font-style: italic;
        font-weight: var(--font-weight-medium);
      }

      /* Register button — primary-blue CTA. Distinct from the green "done"
         pill so a row scan reads cleanly: green ✓ = registered, blue button
         = action needed. Reusing green for the CTA (earlier draft) made every
         unregistered row look "done." */
      .btn-register-cell {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 4px var(--space-2);
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

      .lib-td-actions {
        text-align: right;
        white-space: nowrap;
      }

      /* ── Inline registration expand row ──────────────────────────
         The Register button on a row "opens" beneath itself with an
         AG chip strip + collapsed LOP disclosure. Picking a chip
         commits and clears the expand. Replaces a modal that used to
         own this flow. */
      .lib-tr-expand > .lib-td-expand {
        padding: 0;
        background: color-mix(in srgb, var(--bs-primary) 4%, transparent);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-primary) 18%, transparent);
      }

      .register-inline {
        padding: var(--space-2) var(--space-3);
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      /* A numbered step inside the register expand: a "First,/Then" label line
         stacked tightly above its chip row. The register-inline gap (space-2)
         separates the two steps; the within-step gap (space-1) keeps each label
         glued to its own chips. */
      .register-step {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
      }

      /* Highlighted step header — a primary-tinted pill that hugs its text so
         "First, pick…" / "Then pick…" read as distinct, scannable instructions
         above their chip rows rather than blending into the body copy. */
      .register-step-label {
        align-self: flex-start;
        padding: 2px var(--space-2);
        border-radius: var(--radius-sm);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
      }

      .ag-chip-row {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
      }

      .ag-chip {
        display: inline-flex;
        flex-direction: column;
        align-items: center;
        gap: 2px;
        min-width: 64px;
        padding: 6px var(--space-2);
        border: 1.5px solid color-mix(in srgb, var(--bs-primary) 35%, transparent);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--bs-primary);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        transition: background-color 0.12s ease, border-color 0.12s ease, transform 0.12s ease;
      }

      .ag-chip:hover:not(:disabled) {
        background: var(--bs-primary);
        color: var(--neutral-0);
        border-color: var(--bs-primary);
        transform: translateY(-1px);
      }

      .ag-chip:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      .ag-chip:disabled { opacity: 0.4; cursor: default; transform: none; }

      .ag-chip.is-recommended {
        border-color: var(--bs-success);
        box-shadow: 0 0 0 2px color-mix(in srgb, var(--bs-success) 20%, transparent);
      }

      .ag-chip.is-almost-full { border-color: var(--bs-warning); color: var(--bs-warning); }

      /* Full = waitlist path. Keep clickable; visually distinct from open AGs
         via dashed border + amber accent + uppercase meta label. */
      .ag-chip.is-full {
        border-style: dashed;
        border-color: var(--bs-warning);
        color: var(--bs-warning);
        background: color-mix(in srgb, var(--bs-warning) 6%, transparent);
      }
      .ag-chip.is-full:hover:not(:disabled) {
        background: var(--bs-warning);
        color: var(--neutral-0);
        border-color: var(--bs-warning);
        border-style: solid;
      }
      .ag-chip.is-full .ag-chip-meta {
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }

      /* Selected = the chip the rep has chosen (commits on Submit, not on click).
         Filled primary so it wins over the recommended/almost-full/full accents.
         Placed last so its rules take precedence over the states above. */
      .ag-chip.is-selected,
      .ag-chip.is-selected:hover:not(:disabled) {
        background: var(--bs-primary);
        color: var(--neutral-0);
        border-color: var(--bs-primary);
        border-style: solid;
      }

      .ag-chip-name { font-size: var(--font-size-xs); }
      /* Asterisk flag — age group name literally matches the team's library grad
         year. Red + bold so it reads as the "this is the matching one" cue. */
      .ag-chip-gradyear-match {
        color: var(--bs-danger);
        font-weight: var(--font-weight-bold);
        margin-left: 1px;
      }
      .ag-chip-meta { font-size: 10px; font-weight: var(--font-weight-medium); opacity: 0.85; }

      /* LOP selection — always-visible chip row (step 1), sits above the age
         groups. Stored/previous LOP renders as the .active chip on open. */
      .register-inline .lop-pills {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
      }

      .register-inline .lop-pill {
        flex: 0 1 auto;
        min-width: 44px;
        padding: 4px var(--space-2);
        border: 1.5px solid var(--border-color);
        border-radius: var(--radius-full);
        background: var(--brand-surface);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
        cursor: pointer;
        transition: all 0.12s ease;
      }

      .register-inline .lop-pill:hover { border-color: var(--bs-primary); }
      .register-inline .lop-pill.active {
        border-color: var(--bs-primary);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        font-weight: var(--font-weight-semibold);
      }
      .register-inline .lop-pill:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

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

      /* When a row is the active expand source, highlight its trigger. */
      .btn-register-cell.is-active {
        background: var(--bs-primary);
        color: var(--neutral-0);
        border-color: var(--bs-primary);
      }

      /* Archived table — no thead, italicized rows */
      .lib-table-archived {
        background: color-mix(in srgb, var(--bs-body-color) 2%, transparent);
      }

      .lib-tr.is-archived {
        opacity: 0.78;

        .team-name { color: var(--brand-text-muted); font-style: italic; }
      }

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

      .lib-lock {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 30px;
        height: 30px;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        opacity: 0.55;
      }

      /* ── Status badges ─────────────────────────────────────────────── */
      .lib-badge {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        font-size: 11px;
        font-weight: var(--font-weight-semibold);
        padding: 4px var(--space-2);
        border-radius: var(--radius-full);
        white-space: nowrap;
        flex-shrink: 0;
        max-width: 180px;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      .lib-badge-success {
        background: rgba(var(--bs-success-rgb), 0.12);
        color: var(--bs-success);
      }

      .lib-badge-muted {
        background: rgba(var(--bs-secondary-rgb), 0.1);
        color: var(--brand-text-muted);
      }

      /* ── Register button — success-green to match wizard color story ── */
      .btn-register {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 110px;
        padding: var(--space-1) var(--space-3);
        border: 1.5px solid rgba(var(--bs-success-rgb), 0.4);
        border-radius: var(--radius-sm);
        background: rgba(var(--bs-success-rgb), 0.08);
        color: var(--bs-success);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        flex-shrink: 0;
        white-space: nowrap;
        transition: background-color 0.12s ease, border-color 0.12s ease,
                    transform 0.12s ease, box-shadow 0.12s ease;

        &:hover:not(:disabled) {
          background: var(--bs-success);
          color: var(--neutral-0);
          border-color: var(--bs-success);
          transform: translateY(-1px);
          box-shadow: var(--shadow-sm);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:disabled { opacity: 0.4; cursor: default; }
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
        .lib-icon-btn, .btn-register, .btn-add-team, .btn-flyin-done,
        .lib-section-toggle, .lib-row { transition: none; }
        .btn-register:hover:not(:disabled) { transform: none; }
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
    readonly clubName = input<string>('');
    readonly canRegister = input(false);
    readonly actionInProgress = input(false);
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    readonly lopOptions = input<readonly string[]>([]);
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

    /** True when the event uses LOP options but the rep hasn't picked one. */
    readonly lopRequired = computed(() =>
        this.lopOptions().length > 0 && !this.selectedLop(),
    );

    /** Expanded row is an already-registered team → Submit is a change, not a new reg. */
    readonly editingExisting = computed(() => {
        const id = this.expandedTeamId();
        return id !== null && this.enteredTeams().has(id);
    });

    /** Submit is enabled once an age group is picked and any required LOP is set. */
    readonly canSubmit = computed(() =>
        !this.actionInProgress() && !!this.selectedAgeGroupId() && !this.lopRequired(),
    );

    /** Best-match age group by team grad year — exact match first, then substring. */
    private bestMatchAgeGroupId(team: ClubTeamDto): string {
        const gy = team.clubTeamGradYear;
        if (!gy) return '';
        const ags = this.ageGroups();
        const exact = ags.find(a => a.ageGroupName === gy);
        if (exact) return exact.ageGroupId;
        const contains = ags.find(a => a.ageGroupName.includes(gy));
        return contains?.ageGroupId ?? '';
    }

    /** AG chip data for the currently expanded row. */
    readonly expandedAgChips = computed(() => {
        const teamId = this.expandedTeamId();
        if (teamId === null) return [];
        const team = this.clubTeams().find(t => t.clubTeamId === teamId);
        if (!team) return [];
        const recommendedId = this.bestMatchAgeGroupId(team);
        const gy = team.clubTeamGradYear;
        return this.ageGroups().map(ag => {
            const spotsLeft = Math.max(0, ag.maxTeams - ag.registeredCount);
            return {
                ageGroupId: ag.ageGroupId,
                ageGroupName: ag.ageGroupName,
                spotsLeft,
                isFull: spotsLeft === 0,
                isAlmostFull: spotsLeft > 0 && spotsLeft <= 2,
                isRecommended: ag.ageGroupId === recommendedId,
                // Flag an exact name↔grad-year match so the template can mark it.
                // Not all events name age groups by grad year (e.g. tournaments),
                // so this only fires on a literal match.
                matchesGradYear: !!gy && ag.ageGroupName === gy,
            };
        });
    });

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
        const opts = this.lopOptions();
        const lopCandidate = existing?.levelOfPlay || team.clubTeamLevelOfPlay || '';
        this.selectedLop.set(lopCandidate && opts.includes(lopCandidate) ? lopCandidate : '');
        this.selectedAgeGroupId.set(
            existing ? this.ageGroupIdByName(existing.ageGroupName) : this.bestMatchAgeGroupId(team),
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
     * Active teams in alphabetical order. Stable position is the goal —
     * registering a team should NOT move it (status-priority sort would shift
     * rows mid-session and break the rep's spatial memory of their library).
     * Visual cues (green ✓ vs blue Register button) already surface action
     * state without re-ordering.
     */
    readonly sortedActiveTeams = this.activeTeams;

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

    /**
     * Header status state. Drives chip color, icon, and message — the rep's
     * registration-coverage check at a glance.
     *   - 'empty'           → library is empty (or all-archived); chip hidden, body shows hero
     *   - 'none-registered' → library has teams, none entered for this event (urgent — likely no-show trap)
     *   - 'partial'         → some entered, some not (action needed — see which are unregistered below)
     *   - 'all-registered'  → every active team is entered (reassuring — green ✓)
     */
    readonly statusState = computed<'empty' | 'none-registered' | 'partial' | 'all-registered'>(() => {
        const active = this.activeTeams().length;
        if (active === 0) return 'empty';
        const registered = this.registeredCount();
        if (registered === 0) return 'none-registered';
        if (registered < active) return 'partial';
        return 'all-registered';
    });

    readonly statusIcon = computed(() => {
        switch (this.statusState()) {
            case 'all-registered':  return 'bi-check-circle-fill';
            case 'partial':         return 'bi-exclamation-triangle-fill';
            case 'none-registered': return 'bi-exclamation-octagon-fill';
            default:                return '';
        }
    });

    readonly statusText = computed(() => {
        const total = this.activeTeams().length;
        const reg = this.registeredCount();
        if (this.statusState() === 'empty') return '';
        return `${reg} of ${total} registered`;
    });

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
