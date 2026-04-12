import {
    Component, ChangeDetectionStrategy, OnInit, inject, signal, computed,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { environment } from '@environments/environment';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { LoginComponent } from '@views/auth/login/login.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { SelfRosterPlayerDto } from '@core/api/models/SelfRosterPlayerDto';
import type { SelfRosterUpdateRequestDto } from '@core/api/models/SelfRosterUpdateRequestDto';

interface PlayerEditState {
    uniformNo: string;
    position: string;
    teamId: string;
}

@Component({
    selector: 'app-self-roster-update',
    standalone: true,
    imports: [FormsModule, LoginComponent, ConfirmDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
    <div class="sru-shell">

      <!-- Phase 1: Login -->
      @if (!authenticated()) {
        <div class="welcome-hero">
          <h4 class="welcome-title">
            <i class="bi bi-pencil-square welcome-icon"></i>
            Update Registrations
          </h4>
          <p class="welcome-desc">
            <i class="bi bi-shield-lock me-1"></i>Sign in with your family account to continue
          </p>
        </div>
        <div class="login-wrapper">
          <app-login [embedded]="true" (loginSuccess)="onLoginSuccess()"></app-login>
        </div>
      }

      <!-- Phase 2: Player Edit -->
      @if (authenticated()) {
        <div class="welcome-hero">
          <h4 class="welcome-title">
            <i class="bi bi-pencil-square welcome-icon"></i>
            Update Registrations
          </h4>
          <p class="welcome-desc">
            <i class="bi bi-person-gear me-1"></i>Change team, uniform, or position
            <span class="desc-dot"></span>
            <i class="bi bi-trash3 me-1"></i>Delete a registration
          </p>
        </div>

        @if (loading()) {
          <div class="text-center py-4">
            <span class="spinner-border spinner-border-sm me-2"></span>Loading players...
          </div>
        }

        @if (!loading() && players().length === 0) {
          <div class="sru-empty">
            <i class="bi bi-person-x"></i>
            <strong>No active registrations</strong>
            <span>Your family has no active player registrations for this event.</span>
          </div>
        }

        @for (player of players(); track player.registrationId; let i = $index) {
          <div class="card shadow border-0 card-rounded sru-card">
            <div class="sru-card-header">
              <div class="sru-player-info">
                <i class="bi bi-person-fill sru-player-icon"></i>
                <span class="sru-player-name">{{ player.firstName }} {{ player.lastName }}</span>
              </div>
              <span class="sru-team-pill">{{ player.teamName }}</span>
            </div>
            <div class="card-body">
              <div class="sru-field-grid">
                <div class="field-row">
                  <label class="field-label">Uniform #</label>
                  <input class="field-input"
                         type="text"
                         [ngModel]="edits()[player.registrationId]?.uniformNo ?? ''"
                         (ngModelChange)="updateEdit(player.registrationId, 'uniformNo', $event)"
                         placeholder="Enter uniform number">
                </div>
                <div class="field-row">
                  <label class="field-label">Position</label>
                  <select class="field-select"
                          [ngModel]="edits()[player.registrationId]?.position ?? ''"
                          (ngModelChange)="updateEdit(player.registrationId, 'position', $event)">
                    <option value="">— Select —</option>
                    @for (pos of player.availablePositions; track pos) {
                      <option [value]="pos">{{ pos }}</option>
                    }
                  </select>
                </div>
                <div class="field-row field-row--wide">
                  <label class="field-label">Team</label>
                  <select class="field-select"
                          [ngModel]="edits()[player.registrationId]?.teamId ?? ''"
                          (ngModelChange)="updateEdit(player.registrationId, 'teamId', $event)">
                    @for (team of player.availableTeams; track team.teamId) {
                      <option [value]="team.teamId">
                        {{ team.teamName }}
                        @if (team.maxCount > 0) { ({{ team.currentCount }}/{{ team.maxCount }}) }
                      </option>
                    }
                  </select>
                </div>
              </div>

              <div class="sru-actions">
                <button class="btn btn-primary btn-sm"
                        [disabled]="!isDirty(player.registrationId) || saving()"
                        (click)="savePlayer(player)">
                  @if (saving() && savingId() === player.registrationId) {
                    <span class="spinner-border spinner-border-sm me-1"></span>Saving...
                  } @else {
                    <i class="bi bi-check-lg me-1"></i>Save Changes
                  }
                </button>
                <button class="btn btn-outline-danger btn-sm"
                        [disabled]="saving()"
                        (click)="confirmDelete(player)">
                  <i class="bi bi-trash3 me-1"></i>Delete
                </button>
              </div>
            </div>
          </div>
        }
      }

      <!-- Delete confirmation dialog -->
      @if (deleteTarget()) {
        <confirm-dialog
          title="Delete Registration"
          [message]="'Are you sure you want to delete the registration for ' + deleteTarget()!.firstName + ' ' + deleteTarget()!.lastName + '? This cannot be undone. You will need to re-register.'"
          confirmLabel="Delete Registration"
          confirmVariant="danger"
          (confirmed)="executeDelete()"
          (cancelled)="deleteTarget.set(null)">
        </confirm-dialog>
      }
    </div>
    `,
    styles: [`
      .sru-shell {
        max-width: 720px;
        margin: 0 auto;
        padding: var(--space-4) var(--space-3);
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
      }

      .welcome-hero {
        display: flex;
        flex-direction: column;
        align-items: center;
        text-align: center;
        padding: var(--space-4);
      }
      .welcome-title {
        font-size: var(--font-size-2xl);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
      }
      .welcome-icon {
        font-size: var(--font-size-2xl);
        color: var(--bs-primary);
        margin-right: var(--space-2);
      }
      .welcome-desc {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        margin-top: var(--space-2);
      }
      .desc-dot {
        display: inline-block;
        width: 4px;
        height: 4px;
        border-radius: 50%;
        background: var(--neutral-300);
        vertical-align: middle;
        margin: 0 var(--space-2);
      }

      .login-wrapper {
        max-width: 400px;
        margin: 0 auto;
        width: 100%;
      }

      .sru-card {
        overflow: hidden;
      }
      .sru-card-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-body-color-rgb), 0.025);
        border-bottom: 1px solid var(--border-color);
      }
      .sru-player-info {
        display: flex;
        align-items: center;
        gap: var(--space-2);
      }
      .sru-player-icon {
        font-size: var(--font-size-base);
        color: var(--neutral-400);
      }
      .sru-player-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }
      .sru-team-pill {
        font-size: 11px;
        font-weight: var(--font-weight-medium);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.2);
      }

      .sru-field-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-1) var(--space-4);
        padding: var(--space-3) 0;
      }
      .field-row--wide {
        grid-column: 1 / -1;
      }
      .field-row {
        display: flex;
        flex-direction: column;
        gap: 1px;
      }

      .sru-actions {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding-top: var(--space-2);
        border-top: 1px solid var(--border-color);
      }

      .sru-empty {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-8) var(--space-4);
        color: var(--brand-text-muted);
        text-align: center;
      }
      .sru-empty i {
        font-size: var(--font-size-3xl);
        color: var(--neutral-300);
      }

      @media (max-width: 575.98px) {
        .sru-field-grid {
          grid-template-columns: 1fr;
        }
        .field-row--wide {
          grid-column: auto;
        }
        .sru-card-header {
          flex-direction: column;
          align-items: flex-start;
          gap: var(--space-1);
        }
      }
    `]
})
export class SelfRosterUpdateComponent implements OnInit {
    private readonly http = inject(HttpClient);
    private readonly auth = inject(AuthService);
    private readonly toast = inject(ToastService);
    private readonly route = inject(ActivatedRoute);

    readonly authenticated = signal(false);
    readonly loading = signal(false);
    readonly saving = signal(false);
    readonly savingId = signal<string | null>(null);
    readonly players = signal<SelfRosterPlayerDto[]>([]);
    readonly deleteTarget = signal<SelfRosterPlayerDto | null>(null);

    // Per-player edit state: { [registrationId]: { uniformNo, position, teamId } }
    readonly edits = signal<Record<string, PlayerEditState>>({});
    // Snapshot of initial values for dirty tracking
    private snapshots: Record<string, PlayerEditState> = {};

    ngOnInit(): void {
        this.auth.logoutLocal();
    }

    onLoginSuccess(): void {
        this.authenticated.set(true);
        this.loadPlayers();
    }

    isDirty(regId: string): boolean {
        const current = this.edits()[regId];
        const snap = this.snapshots[regId];
        if (!current || !snap) return false;
        return current.uniformNo !== snap.uniformNo
            || current.position !== snap.position
            || current.teamId !== snap.teamId;
    }

    updateEdit(regId: string, field: keyof PlayerEditState, value: string): void {
        const current = { ...this.edits() };
        current[regId] = { ...current[regId], [field]: value };
        this.edits.set(current);
    }

    savePlayer(player: SelfRosterPlayerDto): void {
        const edit = this.edits()[player.registrationId];
        if (!edit) return;

        this.saving.set(true);
        this.savingId.set(player.registrationId);

        const body: SelfRosterUpdateRequestDto = {
            uniformNo: edit.uniformNo || undefined,
            position: edit.position || undefined,
            teamId: edit.teamId,
        };

        this.http.put(`${this.apiBase()}/self-roster-update/${player.registrationId}`, body)
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.savingId.set(null);
                    // Update snapshot so dirty tracking resets
                    this.snapshots[player.registrationId] = { ...edit };
                    // Update the team pill if team changed
                    const team = player.availableTeams?.find(t => t.teamId === edit.teamId);
                    if (team) {
                        const updated = this.players().map(p =>
                            p.registrationId === player.registrationId
                                ? { ...p, teamName: team.teamName, uniformNo: edit.uniformNo, position: edit.position, teamId: edit.teamId }
                                : p
                        );
                        this.players.set(updated);
                    }
                    this.toast.show('Registration updated.', 'success', 3000);
                },
                error: (err) => {
                    this.saving.set(false);
                    this.savingId.set(null);
                    this.toast.show(err?.error?.message ?? 'Failed to update registration.', 'danger', 5000);
                }
            });
    }

    confirmDelete(player: SelfRosterPlayerDto): void {
        this.deleteTarget.set(player);
    }

    executeDelete(): void {
        const player = this.deleteTarget();
        if (!player) return;
        this.deleteTarget.set(null);
        this.saving.set(true);

        this.http.delete(`${this.apiBase()}/self-roster-update/${player.registrationId}`)
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.players.set(this.players().filter(p => p.registrationId !== player.registrationId));
                    delete this.snapshots[player.registrationId];
                    const current = { ...this.edits() };
                    delete current[player.registrationId];
                    this.edits.set(current);
                    this.toast.show(`Registration for ${player.firstName} ${player.lastName} deleted.`, 'success', 3000);
                },
                error: (err) => {
                    this.saving.set(false);
                    this.toast.show(err?.error?.message ?? 'Failed to delete registration.', 'danger', 5000);
                }
            });
    }

    private loadPlayers(): void {
        this.loading.set(true);
        this.http.get<SelfRosterPlayerDto[]>(`${this.apiBase()}/self-roster-update/players`)
            .subscribe({
                next: (data) => {
                    this.players.set(data);
                    // Build initial edit state + snapshots
                    const editState: Record<string, PlayerEditState> = {};
                    for (const p of data) {
                        const state: PlayerEditState = {
                            uniformNo: p.uniformNo ?? '',
                            position: p.position ?? '',
                            teamId: p.teamId,
                        };
                        editState[p.registrationId] = state;
                        this.snapshots[p.registrationId] = { ...state };
                    }
                    this.edits.set(editState);
                    this.loading.set(false);
                },
                error: (err) => {
                    this.loading.set(false);
                    this.toast.show(err?.error?.message ?? 'Failed to load players.', 'danger', 5000);
                }
            });
    }

    private apiBase(): string {
        try {
            const host = globalThis.location?.host?.toLowerCase?.() ?? '';
            if (host.startsWith('localhost') || host.startsWith('127.0.0.1')) {
                return 'https://localhost:7215/api';
            }
        } catch { /* SSR */ }
        return environment.apiUrl.endsWith('/api') ? environment.apiUrl : `${environment.apiUrl}/api`;
    }
}
