import { ChangeDetectionStrategy, Component, HostListener, computed, input, output, signal } from '@angular/core';
import type { ClubTeamDto } from '@core/api';

/**
 * Library fly-in drawer — picker UI for adding teams to the current event.
 *
 * Mirrors the search-registrations / search-teams drawer pattern (proven, see
 * search-registrations.component.scss). z-index 1050/1049 — do not raise.
 *
 * Owns no domain state; parent feeds rows + flags and handles all mutations.
 */
@Component({
    selector: 'app-library-flyin',
    standalone: true,
    template: `
    @if (isOpen()) {
      <div class="library-backdrop" (click)="onClose()"></div>
    }
    <aside class="library-panel" [class.open]="isOpen()" role="dialog" aria-modal="true" aria-labelledby="library-flyin-title">
      <div class="panel-header">
        <div class="header-top-row">
          <h2 class="panel-title" id="library-flyin-title">
            <i class="bi bi-collection-fill"></i>
            Team Library ({{ activeTeams().length }})
          </h2>
          <button type="button" class="btn-close" aria-label="Close library" (click)="onClose()">&times;</button>
        </div>
        <p class="tip">Your library is permanent — teams you add here carry to every TSIC event.</p>
      </div>

      <div class="panel-body">
        @if (activeTeams().length === 0 && archivedTeams().length === 0) {
          <div class="lib-empty">
            <span class="step-pill">Step 1 of 2</span>
            <i class="bi bi-plus-circle-dotted"></i>
            <strong>Build your Club Library</strong>
            <span class="lib-empty-desc">
              Add every team your club runs &mdash; they're saved across
              <em>every</em> TSIC event you'll participate in.
            </span>
            <div class="lib-empty-note">
              <i class="bi bi-info-circle-fill"></i>
              <span>
                Adding a team here is <strong>not</strong> event registration.
                You'll register library teams for this event next.
              </span>
            </div>
            <button type="button" class="btn btn-primary btn-sm mt-2" (click)="addNew.emit()">
              <i class="bi bi-plus-circle me-1"></i>Add Your First Library Team
            </button>
          </div>
        } @else if (activeTeams().length === 0) {
          <div class="lib-empty">
            <i class="bi bi-archive"></i>
            <strong>All teams are archived</strong>
            <span>Restore a team below, or
              <button type="button" class="lib-link" (click)="addNew.emit()">add a new one</button>
              to start fresh.</span>
          </div>
        } @else {
          <div class="lib-list">
            @for (team of activeTeams(); track team.clubTeamId) {
              <div class="lib-row" [class.lib-row-registered]="isEntered(team.clubTeamId)">
                <i class="bi bi-people-fill lib-icon"></i>
                <span class="lib-name">{{ team.clubTeamName }}</span>
                <span class="lib-actions">
                  @if (team.bHasBeenScheduled) {
                    @if (!isEntered(team.clubTeamId)) {
                      <button type="button" class="lib-icon-btn" title="Archive — hide from library, keep history"
                              [disabled]="actionInProgress()"
                              (click)="archive.emit(team)">
                        <i class="bi bi-box-arrow-in-down"></i>
                      </button>
                    }
                    <i class="bi bi-clock-history lib-lock" title="Has event history — name/grad-year locked"></i>
                  } @else {
                    @if (!isEntered(team.clubTeamId)) {
                      <button type="button" class="lib-icon-btn lib-icon-danger" title="Delete from library"
                              [disabled]="actionInProgress()"
                              (click)="delete.emit(team)">
                        <i class="bi bi-trash"></i>
                      </button>
                    }
                    <button type="button" class="lib-icon-btn" title="Edit library team"
                            [disabled]="actionInProgress()"
                            (click)="edit.emit(team)">
                      <i class="bi bi-pencil"></i>
                    </button>
                  }
                </span>
                @if (isEntered(team.clubTeamId)) {
                  <span class="lib-badge"><i class="bi bi-check-circle-fill me-1"></i>Registered</span>
                } @else if (canRegister()) {
                  <button type="button" class="btn-register"
                          [disabled]="actionInProgress()"
                          (click)="register.emit(team)">
                    Register
                  </button>
                } @else {
                  <span class="lib-badge lib-badge-muted"><i class="bi bi-lock-fill me-1"></i>Closed</span>
                }
              </div>
            }
          </div>

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
                    <i class="bi bi-archive-fill lib-icon"></i>
                    <span class="lib-name">{{ team.clubTeamName }}</span>
                    <span class="lib-actions">
                      <i class="bi bi-clock-history lib-lock" title="Archived — name/grad-year locked"></i>
                      <button type="button" class="lib-icon-btn" title="Restore to library"
                              [disabled]="actionInProgress()"
                              (click)="restore.emit(team)">
                        <i class="bi bi-arrow-counterclockwise"></i>
                      </button>
                    </span>
                    <span class="lib-badge lib-badge-muted"><i class="bi bi-archive me-1"></i>Archived</span>
                  </div>
                }
              </div>
            }
          }
        }
      </div>

      <div class="panel-footer">
        @if (activeTeams().length > 0) {
          <button type="button" class="btn-flyin-add" (click)="addNew.emit()">
            <i class="bi bi-plus-circle me-1"></i>Add Team to Library
          </button>
        }
        <button type="button" class="btn-flyin-done" (click)="onClose()">Done</button>
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
          font-size: 11px;
          color: var(--brand-text-muted);
          line-height: var(--line-height-normal);
        }
      }

      .panel-body {
        flex: 1;
        overflow-y: auto;
        padding: 0;
      }

      .panel-footer {
        flex-shrink: 0;
        padding: var(--space-2) var(--space-3);
        border-top: 1px solid var(--bs-border-color);
        background: var(--bs-body-bg);
        display: flex;
        align-items: center;
        gap: var(--space-2);

        .btn-flyin-done { margin-left: auto; }
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
        transition: background-color 0.1s ease;

        &:hover { background: var(--bs-primary); filter: brightness(1.1); }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
      }

      /* ── Library list rows ─────────────────────────────────────────── */
      .lib-list {
        display: flex;
        flex-direction: column;
      }

      .lib-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        min-height: 44px;
        border-bottom: 1px solid rgba(var(--bs-dark-rgb), 0.04);
        font-size: var(--font-size-sm);

        &:last-child { border-bottom: none; }
      }

      .lib-icon {
        color: rgba(var(--bs-primary-rgb), 0.4);
        font-size: var(--font-size-base);
        flex-shrink: 0;
        width: 18px;
        text-align: center;
      }

      .lib-name {
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        min-width: 0;
        flex: 1;
      }

      .lib-row-registered {
        background: rgba(var(--bs-success-rgb), 0.06);

        .lib-icon { color: var(--bs-success); }
        .lib-name { color: var(--bs-success); }
      }

      .lib-actions {
        display: inline-flex;
        align-items: center;
        justify-content: flex-end;
        gap: 2px;
        width: 64px;
        flex-shrink: 0;
      }

      .lib-icon-btn {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 28px;
        height: 28px;
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
        width: 28px;
        height: 28px;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        opacity: 0.55;
      }

      .lib-badge {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 96px;
        font-size: 11px;
        font-weight: var(--font-weight-semibold);
        padding: 4px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-success-rgb), 0.1);
        color: var(--bs-success);
        white-space: nowrap;
        flex-shrink: 0;
      }

      .lib-badge-muted {
        background: rgba(var(--bs-secondary-rgb), 0.1);
        color: var(--brand-text-muted);
      }

      .btn-register {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 96px;
        padding: var(--space-1) var(--space-3);
        border: 1.5px solid rgba(var(--bs-primary-rgb), 0.3);
        border-radius: var(--radius-sm);
        background: rgba(var(--bs-primary-rgb), 0.06);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        flex-shrink: 0;
        white-space: nowrap;
        transition: background-color 0.1s ease, border-color 0.1s ease;

        &:hover:not(:disabled) {
          background: rgba(var(--bs-primary-rgb), 0.14);
          border-color: var(--bs-primary);
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
        opacity: 0.75;
        .lib-name { color: var(--brand-text-muted); font-style: italic; }
        .lib-icon { color: var(--brand-text-muted); }
      }

      /* ── Empty state ──────────────────────────────────────────────── */
      .lib-empty {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        text-align: center;
        gap: var(--space-2);
        padding: var(--space-6) var(--space-4);
        color: var(--brand-text-muted);

        > i { font-size: 32px; color: var(--bs-primary); opacity: 0.6; }
        strong { color: var(--brand-text); font-size: var(--font-size-base); }
        span { font-size: var(--font-size-sm); }
        em { font-style: italic; }
      }

      .step-pill {
        display: inline-flex;
        align-items: center;
        padding: 2px var(--space-2);
        font-size: 11px;
        font-weight: var(--font-weight-bold);
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--bs-primary);
        background: rgba(var(--bs-primary-rgb), 0.1);
        border-radius: var(--radius-full);
      }

      .lib-empty-desc {
        max-width: 380px;
        line-height: var(--line-height-normal);
      }

      .lib-empty-note {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        margin-top: var(--space-2);
        max-width: 420px;
        background: rgba(var(--bs-warning-rgb), 0.08);
        border: 1px solid rgba(var(--bs-warning-rgb), 0.25);
        border-radius: var(--radius-sm);
        color: var(--brand-text);
        text-align: left;
        font-size: var(--font-size-sm);
        line-height: var(--line-height-normal);

        i {
          color: var(--bs-warning);
          font-size: var(--font-size-base);
          flex-shrink: 0;
          margin-top: 1px;
          opacity: 1;
        }

        strong { color: var(--brand-text); }
      }

      .lib-link {
        background: none;
        border: none;
        padding: 0;
        color: var(--bs-primary);
        text-decoration: underline;
        cursor: pointer;
        font: inherit;
      }

      @media (prefers-reduced-motion: reduce) {
        .library-panel { transition: none; }
        .lib-icon-btn, .btn-register, .btn-flyin-add, .btn-flyin-done, .archived-header { transition: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryFlyinComponent {
    readonly isOpen = input.required<boolean>();
    readonly clubTeams = input.required<ClubTeamDto[]>();
    readonly canRegister = input(false);
    readonly actionInProgress = input(false);
    readonly enteredTeamIds = input<ReadonlySet<number>>(new Set());

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

    isEntered(clubTeamId: number): boolean {
        return this.enteredTeamIds().has(clubTeamId);
    }

    onClose(): void {
        this.closed.emit();
    }

    @HostListener('document:keydown.escape')
    onEscape(): void {
        if (this.isOpen()) this.onClose();
    }
}
