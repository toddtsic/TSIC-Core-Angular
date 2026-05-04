import { ChangeDetectionStrategy, Component, HostListener, computed, input, output, signal } from '@angular/core';
import type { ClubTeamDto } from '@core/api';

export interface RegisteredInfo {
    ageGroupName: string;
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
      <div class="panel-header">
        <div class="header-top-row">
          <h2 class="panel-title" id="library-flyin-title">
            <i class="bi bi-collection-fill"></i>
            Your Team Library
          </h2>
          <button type="button" class="btn-close" aria-label="Close library" (click)="onClose()">&times;</button>
        </div>

        <!-- Operational status line — adapts to library state -->
        @if (activeTeams().length === 0) {
          <p class="tip">Your library is permanent &mdash; teams you add here carry to every TSIC event.</p>
        } @else if (registeredCount() === 0) {
          <p class="tip">
            <strong>{{ activeTeams().length }}</strong>
            {{ activeTeams().length === 1 ? 'team' : 'teams' }} ready
            &mdash; pick to register for <strong>{{ eventName() }}</strong>.
          </p>
        } @else {
          <p class="tip">
            <strong class="tip-success">{{ registeredCount() }}</strong>
            of <strong>{{ activeTeams().length }}</strong>
            registered for <strong>{{ eventName() }}</strong>.
          </p>
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
          <!-- State C — populated active list (with optional archived sub-section) -->
          <div class="lib-list">
            @for (team of sortedActiveTeams(); track team.clubTeamId) {
              @let registered = registeredInfo(team.clubTeamId);
              @let canAct = !registered && canRegister();
              <div class="lib-row"
                   [class.lib-row-registered]="!!registered"
                   [class.lib-row-actionable]="canAct">

                <!-- Left: identity (icon + name + meta sub-line) -->
                <div class="lib-identity">
                  <i class="bi lib-icon"
                     [class.bi-trophy-fill]="!!registered"
                     [class.bi-people-fill]="!registered"></i>
                  <div class="lib-identity-text">
                    <span class="lib-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
                    <span class="lib-meta">
                      <span class="lib-meta-item">
                        <i class="bi bi-mortarboard"></i>
                        {{ team.clubTeamGradYear || '—' }}
                      </span>
                      @if (team.clubTeamLevelOfPlay) {
                        <span class="lib-meta-divider">·</span>
                        <span class="lib-meta-item" [attr.title]="'Level of Play: ' + team.clubTeamLevelOfPlay">
                          LOP {{ formatLop(team.clubTeamLevelOfPlay) }}
                        </span>
                      }
                    </span>
                  </div>
                </div>

                <!-- Middle: management actions (edit / archive / delete) -->
                <span class="lib-actions">
                  @if (team.bHasBeenScheduled) {
                    @if (!registered) {
                      <button type="button" class="lib-icon-btn"
                              title="Archive — hide from library, keep history"
                              [disabled]="actionInProgress()"
                              (click)="archive.emit(team)">
                        <i class="bi bi-box-arrow-in-down"></i>
                      </button>
                    }
                    <i class="bi bi-clock-history lib-lock"
                       title="Has event history — name and grad-year are locked"></i>
                  } @else {
                    @if (!registered) {
                      <button type="button" class="lib-icon-btn lib-icon-danger"
                              title="Delete from library"
                              [disabled]="actionInProgress()"
                              (click)="delete.emit(team)">
                        <i class="bi bi-trash"></i>
                      </button>
                    }
                    <button type="button" class="lib-icon-btn"
                            title="Edit library team"
                            [disabled]="actionInProgress()"
                            (click)="edit.emit(team)">
                      <i class="bi bi-pencil"></i>
                    </button>
                  }
                </span>

                <!-- Right: status badge OR Register button -->
                @if (registered) {
                  <span class="lib-badge lib-badge-success">
                    <i class="bi bi-check-circle-fill me-1"></i>
                    @if (registered.ageGroupName) {
                      Registered: {{ registered.ageGroupName }}
                    } @else {
                      Registered
                    }
                  </span>
                } @else if (canRegister()) {
                  <button type="button" class="btn-register"
                          [disabled]="actionInProgress()"
                          (click)="register.emit(team)">
                    <i class="bi bi-trophy-fill me-1"></i>Register
                  </button>
                } @else {
                  <span class="lib-badge lib-badge-muted">
                    <i class="bi bi-lock-fill me-1"></i>Closed
                  </span>
                }
              </div>
            }
          </div>

          <!-- Archived sub-section (collapsible) -->
          @if (archivedTeams().length > 0) {
            <button type="button" class="archived-header" (click)="toggleArchived()">
              <i class="bi" [class.bi-chevron-right]="!showArchived()" [class.bi-chevron-down]="showArchived()"></i>
              <i class="bi bi-archive-fill"></i>
              Archived ({{ archivedTeams().length }})
              <span class="archived-hint">teams hidden from registration</span>
            </button>
            @if (showArchived()) {
              <div class="lib-list lib-list-archived">
                @for (team of archivedTeams(); track team.clubTeamId) {
                  <div class="lib-row lib-row-archived">
                    <div class="lib-identity">
                      <i class="bi bi-archive-fill lib-icon"></i>
                      <div class="lib-identity-text">
                        <span class="lib-name">{{ team.clubTeamName }}</span>
                        <span class="lib-meta">
                          <span class="lib-meta-item">
                            <i class="bi bi-mortarboard"></i>
                            {{ team.clubTeamGradYear || '—' }}
                          </span>
                          @if (team.clubTeamLevelOfPlay) {
                            <span class="lib-meta-divider">·</span>
                            <span class="lib-meta-item">LOP {{ formatLop(team.clubTeamLevelOfPlay) }}</span>
                          }
                        </span>
                      </div>
                    </div>
                    <span class="lib-actions">
                      <i class="bi bi-clock-history lib-lock" title="Archived — name and grad-year locked"></i>
                      <button type="button" class="lib-icon-btn"
                              title="Restore to active library"
                              [disabled]="actionInProgress()"
                              (click)="restore.emit(team)">
                        <i class="bi bi-arrow-counterclockwise"></i>
                      </button>
                    </span>
                    <span class="lib-badge lib-badge-muted">
                      <i class="bi bi-archive me-1"></i>Archived
                    </span>
                  </div>
                }
              </div>
            }
          }
        }
      </div>

      <!-- ── Footer ─────────────────────────────────────────────────── -->
      <div class="panel-footer">
        @if (showNoneRegisteredWarning()) {
          <div class="footer-warning">
            <i class="bi bi-exclamation-triangle-fill"></i>
            <span>
              You have <strong>{{ activeTeams().length }}</strong>
              {{ activeTeams().length === 1 ? 'team' : 'teams' }}
              in your library but <strong>none registered</strong> for
              {{ eventName() }} yet.
            </span>
          </div>
        }
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
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid var(--bs-border-color);
        background: var(--bs-body-bg);
        flex-shrink: 0;

        .header-top-row {
          display: flex;
          align-items: center;
          justify-content: space-between;
        }

        .panel-title {
          margin: 0;
          display: inline-flex;
          align-items: center;
          gap: var(--space-2);
          font-size: var(--font-size-base);
          font-weight: var(--font-weight-semibold);
          color: var(--bs-body-color);
          line-height: 1;

          i { color: var(--bs-primary); font-size: var(--font-size-lg); }
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

          &:hover { opacity: 1; }

          &:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
            border-radius: var(--radius-sm);
          }
        }

        .tip {
          margin: var(--space-1) 0 0;
          font-size: var(--font-size-xs);
          color: var(--brand-text-muted);
          line-height: var(--line-height-normal);

          strong { color: var(--brand-text); font-weight: var(--font-weight-bold); }
          .tip-success { color: var(--bs-success); }
        }
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
        padding: var(--space-2) var(--space-3);
        border-top: 1px solid var(--bs-border-color);
        background: var(--bs-body-bg);
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

      .footer-warning {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-warning-rgb), 0.1);
        border: 1px solid rgba(var(--bs-warning-rgb), 0.3);
        border-radius: var(--radius-sm);
        font-size: var(--font-size-xs);
        color: var(--brand-text);
        line-height: var(--line-height-normal);

        > i {
          color: var(--bs-warning);
          font-size: var(--font-size-base);
          flex-shrink: 0;
          margin-top: 1px;
        }

        strong { color: var(--brand-text); font-weight: var(--font-weight-bold); }
      }

      .btn-flyin-add {
        display: inline-flex;
        align-items: center;
        padding: var(--space-2) var(--space-3);
        background: transparent;
        border: 1px solid rgba(var(--bs-primary-rgb), 0.3);
        border-radius: var(--radius-sm);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        transition: background-color 0.1s ease, border-color 0.1s ease;

        &:hover { background: rgba(var(--bs-primary-rgb), 0.08); border-color: var(--bs-primary); }

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

      /* ── Library list rows ─────────────────────────────────────────── */
      .lib-list {
        display: flex;
        flex-direction: column;
      }

      .lib-row {
        display: grid;
        grid-template-columns: 1fr auto auto;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        min-height: 56px;
        border-bottom: 1px solid rgba(var(--bs-dark-rgb), 0.04);
        transition: background-color 0.1s ease;

        &:last-child { border-bottom: none; }
      }

      .lib-row-actionable:hover {
        background: rgba(var(--bs-success-rgb), 0.04);
      }

      .lib-identity {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        min-width: 0;
      }

      .lib-icon {
        font-size: var(--font-size-lg);
        flex-shrink: 0;
        width: 22px;
        text-align: center;
        color: rgba(var(--bs-primary-rgb), 0.55);
      }

      .lib-identity-text {
        display: flex;
        flex-direction: column;
        gap: 1px;
        min-width: 0;
      }

      .lib-name {
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        font-size: var(--font-size-sm);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        line-height: 1.25;
      }

      .lib-meta {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        font-size: 11px;
        color: var(--brand-text-muted);
        line-height: 1.2;

        i { font-size: 10px; opacity: 0.75; }
      }

      .lib-meta-item {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        white-space: nowrap;
      }

      .lib-meta-divider { opacity: 0.55; }

      /* Registered row — green tint, trophy icon, success palette */
      .lib-row-registered {
        background: rgba(var(--bs-success-rgb), 0.05);

        .lib-icon { color: var(--bs-success); }
        .lib-name { color: var(--bs-success); }
      }

      .lib-actions {
        display: inline-flex;
        align-items: center;
        justify-content: flex-end;
        gap: 4px;
        flex-shrink: 0;
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

      .lib-icon-danger:hover:not(:disabled) {
        background: rgba(var(--bs-danger-rgb), 0.08);
        border-color: rgba(var(--bs-danger-rgb), 0.25);
        color: var(--bs-danger);
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
    readonly eventName = input('this event');

    readonly closed = output<void>();
    readonly register = output<ClubTeamDto>();
    readonly addNew = output<void>();
    readonly edit = output<ClubTeamDto>();
    readonly archive = output<ClubTeamDto>();
    readonly delete = output<ClubTeamDto>();
    readonly restore = output<ClubTeamDto>();

    /** Local UI state for collapsible archived sub-section (default collapsed). */
    readonly showArchived = signal(false);
    toggleArchived(): void { this.showArchived.set(!this.showArchived()); }

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
     * Active teams sorted by status priority — surfaces what the rep can act
     * on. Order: not-yet-registered (Register button) → registered → closed.
     * Within each group, alphabetical.
     */
    readonly sortedActiveTeams = computed(() => {
        const entered = this.enteredTeams();
        const canReg = this.canRegister();
        const statusOf = (t: ClubTeamDto): number => {
            if (entered.has(t.clubTeamId)) return 1;       // Registered (done)
            if (canReg) return 0;                          // Action needed (top)
            return 2;                                      // Closed (informational)
        };
        return [...this.activeTeams()].sort((a, b) => {
            const sa = statusOf(a);
            const sb = statusOf(b);
            if (sa !== sb) return sa - sb;
            return a.clubTeamName.localeCompare(b.clubTeamName);
        });
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
        if (this.isOpen()) this.onClose();
    }
}
