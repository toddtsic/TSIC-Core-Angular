import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { PlayerFormModalComponent } from './player-form-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { environment } from '@environments/environment';

/**
 * Player Selection step — shows family players as checkboxes.
 * Registered players are locked (checked, disabled).
 * Unregistered players can be toggled on/off.
 */
@Component({
    selector: 'app-prw-player-selection-step',
    standalone: true,
    imports: [DatePipe, PlayerFormModalComponent, ConfirmDialogComponent],
    template: `
    <!-- Centered hero -->
    <div class="welcome-hero">
      <h4 class="welcome-title"><i class="bi bi-people-fill welcome-icon"></i> Choose Your Players</h4>
      <p class="welcome-desc">
        <i class="bi bi-check-square me-1"></i>Check players to register
        <span class="desc-dot"></span>
        <i class="bi bi-pencil me-1"></i>Edit player details
      </p>
    </div>

    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-2 d-flex align-items-center">
        <h5 class="mb-0 fw-semibold" style="font-size: var(--font-size-base)">Your Players</h5>
        <button type="button" class="btn btn-link btn-sm ms-auto p-0 text-decoration-none"
                (click)="openFamilyEdit()">
          <i class="bi bi-pencil-square me-1"></i>Edit Family Account/Players
        </button>
      </div>
      <div class="card-body pt-3">
        @if (state.familyPlayers.familyPlayersLoading()) {
          <div class="text-center py-4">
            <div class="spinner-border text-primary" role="status">
              <span class="visually-hidden">Loading players...</span>
            </div>
          </div>
        } @else if (state.familyPlayers.familyPlayers().length === 0) {
          <div class="wizard-empty-state">
            <i class="bi bi-person-plus-fill"></i>
            <strong>No players on this account yet</strong>
            <span>Use <em>Edit Family Account/Players</em> above to add your first player.</span>
          </div>
        } @else {
          <div class="player-list">
            @for (player of state.familyPlayers.familyPlayers(); track player.playerId) {
              <label class="player-row"
                [class.is-selected]="player.selected && !player.registered"
                [class.is-registered]="player.registered"
                (dblclick)="selectAndContinue(player.playerId, player.registered)">
                <input
                  type="checkbox"
                  class="player-check"
                  [checked]="player.selected || player.registered"
                  [disabled]="player.registered"
                  (change)="toggle(player.playerId)"
                  [attr.aria-label]="'Select ' + player.firstName + ' ' + player.lastName" />
                <i class="bi player-icon"
                  [class.bi-person-fill]="!player.registered"
                  [class.bi-person-check-fill]="player.registered"></i>
                <div class="player-info">
                  <span class="player-name">{{ player.firstName }} {{ player.lastName }}</span>
                  @if (player.dob) {
                    <span class="player-dob">{{ player.dob | date:'MM/dd/yyyy' }}</span>
                  }
                </div>
                @if (player.registered) {
                  <span class="reg-badge">
                    <i class="bi bi-check-circle-fill me-1"></i>Registered
                  </span>
                }
                <span class="action-group">
                  <button type="button" class="btn-action"
                          (click)="openEditPlayer(player); $event.preventDefault(); $event.stopPropagation()"
                          [attr.aria-label]="'Edit ' + player.firstName">
                    <i class="bi bi-pencil"></i>
                  </button>
                  @if (!isProd && player.registered) {
                    <button type="button" class="btn-action btn-action-danger"
                            (click)="deleteRegistration(player); $event.preventDefault(); $event.stopPropagation()"
                            [disabled]="deleting()"
                            [attr.aria-label]="'Delete registration for ' + player.firstName">
                      <i class="bi bi-trash"></i>
                    </button>
                  }
                </span>
              </label>
            }
          </div>
        }
      </div>
    </div>

    <!-- Player add/edit modal -->
    @if (showPlayerModal()) {
      <app-player-form-modal
        [mode]="playerModalMode()"
        [playerId]="editingPlayerId()"
        [initialData]="editingPlayerData()"
        (saved)="onPlayerSaved()"
        (closed)="showPlayerModal.set(false)" />
    }

    <!-- Dev-only delete confirmation -->
    @if (showDeleteConfirm()) {
      <confirm-dialog
        [title]="'DEV ONLY: Delete Registration'"
        [message]="'Delete registration for ' + deletingPlayerName() + '? This will permanently remove the registration and all payment records.'"
        confirmLabel="Delete"
        confirmVariant="danger"
        (confirmed)="confirmDelete()"
        (cancelled)="showDeleteConfirm.set(false)" />
    }
  `,
    styles: [`
      .player-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      .player-row {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        border-radius: var(--radius-md);
        border: 1px solid var(--border-color);
        background: var(--brand-surface);
        cursor: pointer;
        transition: border-color 0.15s ease, background-color 0.15s ease, box-shadow 0.15s ease;

        &:hover:not(.is-registered) {
          border-color: rgba(var(--bs-primary-rgb), 0.4);
          background: rgba(var(--bs-primary-rgb), 0.03);
        }

        &.is-selected {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.06);
          box-shadow: 0 0 0 1px rgba(var(--bs-primary-rgb), 0.15);
        }

        &.is-registered {
          border-color: rgba(var(--bs-success-rgb), 0.25);
          background: rgba(var(--bs-success-rgb), 0.05);
          cursor: default;
          opacity: 0.75;
        }
      }

      /* Custom checkbox */
      .player-check {
        appearance: none;
        width: 20px;
        height: 20px;
        min-width: 20px;
        border: 2px solid var(--neutral-300);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        cursor: pointer;
        transition: border-color 0.15s ease, background-color 0.15s ease;
        position: relative;

        &:checked {
          background: var(--bs-primary);
          border-color: var(--bs-primary);

          &::after {
            content: '';
            position: absolute;
            top: 2px;
            left: 5px;
            width: 6px;
            height: 10px;
            border: solid var(--neutral-0);
            border-width: 0 2px 2px 0;
            transform: rotate(45deg);
          }
        }

        &:disabled {
          cursor: default;

          &:checked {
            background: var(--bs-success);
            border-color: var(--bs-success);
          }
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
      }

      .player-icon {
        font-size: var(--font-size-xl);
        color: var(--neutral-400);

        .is-selected & { color: var(--bs-primary); }
        .is-registered & { color: var(--bs-success); }
      }

      .player-info {
        flex: 1;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: 2px;
      }

      .player-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      .player-dob {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      .reg-badge {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
        white-space: nowrap;
      }

      .action-group {
        display: flex;
        gap: var(--space-1);
        flex-shrink: 0;
      }

      .btn-action {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 28px;
        height: 28px;
        border: 1px solid var(--border-color);
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        cursor: pointer;
        transition: color 0.15s, border-color 0.15s, background 0.15s;

        &:hover {
          color: var(--bs-primary);
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.06);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:disabled {
          opacity: 0.4;
          cursor: not-allowed;
        }
      }

      .btn-action-danger:hover {
        color: var(--bs-danger);
        border-color: var(--bs-danger);
        background: rgba(var(--bs-danger-rgb), 0.06);
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerSelectionStepComponent {
    readonly state = inject(PlayerWizardStateService);
    private readonly http = inject(HttpClient);
    private readonly toast = inject(ToastService);
    private readonly router = inject(Router);
    readonly advance = output<void>();
    readonly hasRegistered = computed(() =>
        this.state.familyPlayers.familyPlayers().some(p => p.registered));

    readonly isProd = environment.production;
    readonly deleting = signal(false);

    // ── Player modal ────────────────────────────────────────────────
    readonly showPlayerModal = signal(false);
    readonly playerModalMode = signal<'add' | 'edit'>('add');
    readonly editingPlayerId = signal<string | null>(null);
    readonly editingPlayerData = signal<{ firstName?: string; lastName?: string; gender?: string; dob?: string } | null>(null);

    toggle(playerId: string): void {
        this.state.togglePlayerSelection(playerId);
    }

    selectAndContinue(playerId: string, registered: boolean): void {
        if (registered) return;
        if (!this.state.familyPlayers.familyPlayers().find(p => p.playerId === playerId)?.selected) {
            this.state.togglePlayerSelection(playerId);
        }
        this.advance.emit();
    }

    openEditPlayer(player: { playerId: string; firstName: string; lastName: string; gender: string; dob?: string | null }): void {
        this.playerModalMode.set('edit');
        this.editingPlayerId.set(player.playerId);
        this.editingPlayerData.set({
            firstName: player.firstName,
            lastName: player.lastName,
            gender: player.gender,
            dob: player.dob ?? undefined,
        });
        this.showPlayerModal.set(true);
    }

    onPlayerSaved(): void {
        this.showPlayerModal.set(false);
        this.refreshPlayers();
    }

    openFamilyEdit(): void {
        const jobPath = this.state.jobCtx.jobPath();
        if (!jobPath) return;
        const returnUrl = `/${jobPath}/registration/player?step=players`;
        this.router.navigate([`/${jobPath}/registration/family`], {
            queryParams: { step: 'children', returnUrl },
        });
    }

    // ── Dev delete confirmation ────────────────────────────────────
    readonly showDeleteConfirm = signal(false);
    readonly deletingPlayerName = signal('');
    private _pendingDeleteRegId: string | null = null;

    deleteRegistration(player: { playerId: string; firstName: string; lastName: string; priorRegistrations?: { registrationId: string; active: boolean }[] }): void {
        if (this.isProd || this.deleting()) return;
        const reg = player.priorRegistrations?.find(r => r.active) ?? player.priorRegistrations?.[0];
        if (!reg) {
            this.toast.show('No registration found to delete', 'warning', 3000);
            return;
        }
        this._pendingDeleteRegId = reg.registrationId;
        this.deletingPlayerName.set(`${player.firstName} ${player.lastName}`);
        this.showDeleteConfirm.set(true);
    }

    confirmDelete(): void {
        this.showDeleteConfirm.set(false);
        if (!this._pendingDeleteRegId) return;
        const regId = this._pendingDeleteRegId;
        const name = this.deletingPlayerName();
        this._pendingDeleteRegId = null;

        this.deleting.set(true);
        this.http.delete(`${environment.apiUrl}/dev/registration/${regId}`).subscribe({
            next: () => {
                this.deleting.set(false);
                this.toast.show(`Registration deleted for ${name}`, 'success', 3000);
                // Full reset + re-initialize so constraint type, metadata, etc. reload
                const jobPath = this.state.jobCtx.jobPath();
                this.state.reset();
                if (jobPath) {
                    this.state.initialize(jobPath);
                }
            },
            error: (err) => {
                this.deleting.set(false);
                const msg = err?.error?.message || err?.message || 'Delete failed';
                this.toast.show(msg, 'danger', 5000);
            },
        });
    }

    private refreshPlayers(): void {
        const jobPath = this.state.jobCtx.jobPath();
        const apiBase = this.state.jobCtx.resolveApiBase();
        if (jobPath && apiBase) {
            this.state.familyPlayers.loadFamilyPlayersOnce(jobPath, apiBase);
        }
    }
}
