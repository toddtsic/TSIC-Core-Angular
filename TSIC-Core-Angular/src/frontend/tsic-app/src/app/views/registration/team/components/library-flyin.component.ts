import { ChangeDetectionStrategy, Component, HostListener, computed, input, output, signal } from '@angular/core';
import type { ClubTeamDto } from '@core/api';

export interface RegisteredInfo {
    ageGroupName: string;
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
    @if (isOpen()) {
      <div class="library-backdrop" (click)="onClose()"></div>
    }
    <aside class="library-panel" [class.open]="isOpen()" role="dialog" aria-modal="true" aria-labelledby="library-flyin-title">

      <!-- ── Header ─────────────────────────────────────────────────── -->
      <!-- Identity moment + ALERT chip (not a status chip). The chip only
           appears when there's a problem to flag — partial coverage or zero
           registered. All-registered state = no chip; row-level "Registered"
           badges already say everything's fine, narrating it again at the
           top would be redundant and gets worse as team count grows. -->
      <div class="panel-header">
        <div class="header-top-row">
          <div class="header-identity">
            <i class="bi bi-collection-fill panel-eyebrow-icon" aria-hidden="true"></i>
            <div class="header-identity-text">
              <p class="panel-eyebrow">Team Library</p>
              <h2 class="panel-title" id="library-flyin-title">Your re-usable Club Team Library</h2>
            </div>
          </div>
          <button type="button" class="btn-close" aria-label="Close library" (click)="onClose()">&times;</button>
        </div>

        @if (statusState() === 'partial' || statusState() === 'none-registered') {
          <div class="status-chip" [class]="'status-chip-' + statusState()">
            <i class="bi" [class]="statusIcon()" aria-hidden="true"></i>
            <span class="status-chip-text">{{ statusText() }}</span>
          </div>
        }
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
          <!-- State C — populated active list rendered as audit table -->
          <table class="lib-table">
            <thead>
              <tr>
                <th class="lib-th-team">Lib-Team</th>
                <th class="lib-th-age">Lib-GradYr</th>
                <th class="lib-th-lop">Lib-LOP</th>
                <th class="lib-th-status">Reg AgeGroup/LOP</th>
                <th class="lib-th-actions">Manage</th>
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
                  <td class="lib-td-lop">{{ formatLop(team.clubTeamLevelOfPlay) || '—' }}</td>
                  <td class="lib-td-status">
                    @if (registered) {
                      @let regDisplay = formatRegisteredDisplay(registered);
                      <span class="status-yes">
                        <i class="bi bi-check-circle-fill" aria-hidden="true"></i>
                        @if (regDisplay) {
                          <span class="status-yes-detail">{{ regDisplay }}</span>
                        }
                      </span>
                    } @else if (canRegister()) {
                      <button type="button" class="btn-register-cell"
                              [disabled]="actionInProgress()"
                              (click)="register.emit(team)">
                        <i class="bi bi-trophy-fill" aria-hidden="true"></i>
                        <span>Register</span>
                      </button>
                    } @else {
                      <span class="status-closed">
                        <i class="bi bi-lock-fill" aria-hidden="true"></i>
                        Closed
                      </span>
                    }
                  </td>
                  <td class="lib-td-actions">
                    <div class="lib-menu-anchor">
                      <button type="button" class="lib-kebab"
                              [class.is-open]="openMenuTeamId() === team.clubTeamId"
                              [disabled]="actionInProgress()"
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
              }
            </tbody>
          </table>

          @if (activeTeams().length <= 3) {
            <div class="lib-tip" role="note">
              <i class="bi bi-lightbulb-fill" aria-hidden="true"></i>
              <div class="lib-tip-text">
                <strong>Library teams carry across events.</strong>
                Add a team once and it stays available for every TSIC event you
                participate in &mdash; you'll never re-enter it.
              </div>
            </div>
          }

          <!-- Archived sub-section (collapsible) -->
          @if (archivedTeams().length > 0) {
            <button type="button" class="archived-header" (click)="toggleArchived()">
              <i class="bi" [class.bi-chevron-right]="!showArchived()" [class.bi-chevron-down]="showArchived()"></i>
              <i class="bi bi-archive-fill"></i>
              Archived ({{ archivedTeams().length }})
              <span class="archived-hint">teams hidden from registration</span>
            </button>
            @if (showArchived()) {
              <table class="lib-table lib-table-archived">
                <tbody>
                  @for (team of archivedTeams(); track team.clubTeamId) {
                    <tr class="lib-tr is-archived">
                      <td class="lib-td-team">
                        <span class="team-name">{{ team.clubTeamName }}</span>
                      </td>
                      <td class="lib-td-age">{{ team.clubTeamGradYear || '—' }}</td>
                      <td class="lib-td-lop">{{ formatLop(team.clubTeamLevelOfPlay) || '—' }}</td>
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
            }
          }
        }
      </div>

      <!-- ── Footer ─────────────────────────────────────────────────── -->
      <!-- Footer warning was dropped — the header status chip carries the
           same signal (and louder, since it's at the top of the panel). -->
      <div class="panel-footer">
        <div class="footer-buttons">
          @if (activeTeams().length > 0) {
            <button type="button" class="btn-flyin-add" (click)="addNew.emit()">
              <i class="bi bi-plus-circle me-1"></i>Add Team to Library
            </button>
          }
          <button type="button"
                  class="btn-flyin-done"
                  [class.btn-flyin-done-warning]="showNoneRegisteredWarning()"
                  (click)="onClose()">
            {{ doneLabel() }}
          </button>
        </div>
      </div>
    </aside>
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
        width: 520px;
        max-width: 100vw;
        height: 100vh;
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

      /* ── Header ────────────────────────────────────────────────────── */
      .panel-header {
        padding: var(--space-3);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-primary) 22%, transparent);
        background: color-mix(in srgb, var(--bs-primary) 6%, var(--bs-body-bg));
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        gap: var(--space-3);

        .header-top-row {
          display: flex;
          align-items: flex-start;
          justify-content: space-between;
          gap: var(--space-2);
        }

        .header-identity {
          display: flex;
          align-items: center;
          gap: var(--space-2);
          min-width: 0;
        }

        .panel-eyebrow-icon {
          color: var(--bs-primary);
          font-size: 1.5rem;
          line-height: 1;
          flex-shrink: 0;
        }

        .header-identity-text {
          display: flex;
          flex-direction: column;
          gap: 1px;
          min-width: 0;
        }

        .panel-eyebrow {
          margin: 0;
          font-size: 11px;
          font-weight: var(--font-weight-bold);
          letter-spacing: 0.08em;
          text-transform: uppercase;
          color: var(--bs-primary);
        }

        .panel-title {
          margin: 0;
          font-size: var(--font-size-sm);
          font-weight: var(--font-weight-medium);
          color: var(--brand-text-muted);
          line-height: 1.2;
        }

        .btn-close {
          padding: 2px 8px;
          background: transparent;
          border: none;
          font-size: var(--font-size-xl);
          line-height: 1;
          color: var(--brand-text);
          opacity: 0.55;
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

      /* ── Status chip — registration coverage check, state-driven ──── */
      .status-chip {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border-radius: var(--radius-md);
        border: 1px solid transparent;
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        line-height: var(--line-height-normal);

        > i {
          flex-shrink: 0;
          font-size: 1.1em;
          line-height: 1.2;
        }
      }

      .status-chip-text { min-width: 0; }

      .status-chip-all-registered {
        background: color-mix(in srgb, var(--bs-success) 12%, transparent);
        border-color: color-mix(in srgb, var(--bs-success) 30%, transparent);
        color: var(--bs-success);
      }

      .status-chip-partial {
        background: color-mix(in srgb, var(--bs-warning) 14%, transparent);
        border-color: color-mix(in srgb, var(--bs-warning) 35%, transparent);
        color: color-mix(in srgb, var(--bs-warning) 70%, var(--brand-text));
      }

      .status-chip-none-registered {
        background: color-mix(in srgb, var(--bs-danger) 12%, transparent);
        border-color: color-mix(in srgb, var(--bs-danger) 35%, transparent);
        color: var(--bs-danger);
      }

      /* ── Body container ────────────────────────────────────────────── */
      .panel-body {
        flex: 1;
        overflow-y: auto;
        padding: 0;
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

      /* Add Team — primary action of the panel. Solid bordered + filled tint
         so it reads as a real button, not a quiet text link. */
      .btn-flyin-add {
        display: inline-flex;
        align-items: center;
        padding: var(--space-2) var(--space-3);
        background: color-mix(in srgb, var(--bs-primary) 10%, transparent);
        border: 1.5px solid var(--bs-primary);
        border-radius: var(--radius-sm);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        transition: background-color 0.1s ease, border-color 0.1s ease;

        &:hover {
          background: color-mix(in srgb, var(--bs-primary) 20%, transparent);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
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

      /* ── Tip card (fills sparse-list dead space + reinforces the
         library-vs-event-registration distinction) ────────────────── */
      .lib-tip {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        margin: var(--space-3);
        padding: var(--space-3);
        background: color-mix(in srgb, var(--bs-primary) 6%, transparent);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 18%, transparent);
        border-radius: var(--radius-md);

        > i {
          color: var(--bs-primary);
          font-size: 1.25rem;
          flex-shrink: 0;
          line-height: 1.2;
        }
      }

      .lib-tip-text {
        font-size: var(--font-size-xs);
        line-height: var(--line-height-normal);
        color: var(--brand-text-muted);

        strong {
          display: block;
          color: var(--brand-text);
          font-weight: var(--font-weight-semibold);
          margin-bottom: 2px;
        }
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
        position: sticky;
        top: 0;
        z-index: 1;
        padding: var(--space-2) var(--space-2);
        text-align: left;
        font-size: 10px;
        font-weight: var(--font-weight-bold);
        letter-spacing: 0.08em;
        text-transform: uppercase;
        color: var(--brand-text-muted);
        background: color-mix(in srgb, var(--bs-body-color) 5%, var(--bs-body-bg));
        border-bottom: 1px solid var(--border-color);
        white-space: nowrap;
      }

      /* Column widths sized for the 520px panel.
         Lib-* columns hold library-intrinsic data; Reg column holds this
         event's registration data. Headers carry the prefix so the rep can
         tell which axis each cell is on. */
      .lib-th-team    { width: auto; }
      .lib-th-age     { width: 80px; text-align: center !important; }
      .lib-th-lop     { width: 60px; text-align: center !important; }
      .lib-th-status  { width: 130px; text-align: left !important; }
      .lib-th-actions { width: 56px; text-align: center !important; }

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
      .lib-td-lop {
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

      /* ── Status column — three states ───────────────────────────── */
      .status-yes {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        color: var(--bs-success);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;

        > i { font-size: 1em; }
      }

      .status-yes-detail {
        font-size: 11px;
        font-weight: var(--font-weight-medium);
      }

      .status-closed {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);

        > i { font-size: 0.95em; }
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

      /* ── Archived sub-section ─────────────────────────────────────── */
      .archived-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-dark-rgb), 0.03);
        border-top: 1px solid var(--border-color);
        border-bottom: 1px solid var(--border-color);
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        cursor: pointer;
        transition: background-color 0.1s ease;

        &:hover { background: rgba(var(--bs-dark-rgb), 0.05); }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
      }

      .archived-hint {
        margin-left: auto;
        text-transform: none;
        letter-spacing: 0;
        font-weight: var(--font-weight-normal);
        font-size: 11px;
        opacity: 0.75;
      }

      .lib-list-archived {
        background: rgba(var(--bs-dark-rgb), 0.015);
      }

      .lib-row-archived {
        opacity: 0.78;

        .lib-name { color: var(--brand-text-muted); font-style: italic; }
        .lib-icon { color: var(--brand-text-muted); }
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
        .lib-icon-btn, .btn-register, .btn-flyin-add, .btn-flyin-done,
        .archived-header, .lib-row { transition: none; }
        .btn-register:hover:not(:disabled) { transform: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryFlyinComponent {
    readonly isOpen = input.required<boolean>();
    readonly clubTeams = input.required<ClubTeamDto[]>();
    readonly canRegister = input(false);
    readonly actionInProgress = input(false);
    /** Map of clubTeamId → registration info. Drives the Registered badge content. */
    readonly enteredTeams = input<ReadonlyMap<number, RegisteredInfo>>(new Map());

    readonly closed = output<void>();
    readonly register = output<ClubTeamDto>();
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

    /**
     * Compact display for the Reg AgeGroup/LOP cell — "U10 / 3", "U10", "3",
     * or empty when both missing. Falls back gracefully when only one is set.
     */
    formatRegisteredDisplay(info: RegisteredInfo): string {
        const age = info.ageGroupName?.trim() ?? '';
        const lop = this.formatLop(info.levelOfPlay);
        if (age && lop) return `${age} / ${lop}`;
        return age || lop;
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
        if (this.isOpen()) this.onClose();
    }
}
