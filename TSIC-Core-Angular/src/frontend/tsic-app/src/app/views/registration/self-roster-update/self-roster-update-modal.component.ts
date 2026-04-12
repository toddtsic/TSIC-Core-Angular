import {
    Component, ChangeDetectionStrategy, inject, signal, computed,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { environment } from '@environments/environment';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { SelfRosterUpdateModalService } from './self-roster-update-modal.service';
import type { SelfRosterPlayerDto } from '@core/api/models/SelfRosterPlayerDto';
import type { SelfRosterTeamOptionDto } from '@core/api/models/SelfRosterTeamOptionDto';
import type { SelfRosterUpdateRequestDto } from '@core/api/models/SelfRosterUpdateRequestDto';

type Phase = 'login' | 'loading' | 'empty' | 'edit';

interface PlayerEditState {
    uniformNo: string;
    position: string;
    teamId: string;
}

@Component({
    selector: 'app-self-roster-update-modal',
    standalone: true,
    imports: [FormsModule, TsicDialogComponent, ConfirmDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
    <tsic-dialog [open]="true" size="" (requestClose)="close()">
      <div class="modal-content sru-modal">

        <div class="modal-header">
          <h5 class="modal-title">
            <i class="bi bi-pencil-square me-2"></i>
            Update Registrations
          </h5>
          <button type="button" class="btn-close" aria-label="Close" (click)="close()"></button>
        </div>

        <div class="modal-body sru-body">

          @if (phase() === 'login') {
            <p class="sru-subtitle">Sign in with your family account to update your player registrations.</p>
            <form (ngSubmit)="submitLogin()" #loginForm="ngForm">
              <div class="field-row mb-2">
                <label class="field-label" for="sru-user">Username</label>
                <input id="sru-user" class="field-input" type="text" name="username"
                       [(ngModel)]="username" required autocomplete="username" autofocus>
              </div>
              <div class="field-row mb-3">
                <label class="field-label" for="sru-pass">Password</label>
                <input id="sru-pass" class="field-input" type="password" name="password"
                       [(ngModel)]="password" required autocomplete="current-password">
              </div>
              @if (loginError()) {
                <div class="field-error mb-2">{{ loginError() }}</div>
              }
              <button type="submit" class="btn btn-primary w-100"
                      [disabled]="loggingIn() || !username || !password">
                @if (loggingIn()) {
                  <span class="spinner-border spinner-border-sm me-1"></span>Signing in...
                } @else {
                  Sign In
                }
              </button>
            </form>
          }

          @if (phase() === 'loading') {
            <div class="sru-loading">
              <span class="spinner-border spinner-border-sm me-2"></span>
              Loading registrations...
            </div>
          }

          @if (phase() === 'empty') {
            <div class="sru-empty">
              <i class="bi bi-person-x"></i>
              <strong>No active registrations</strong>
              <span>Your family has no active player registrations for this event.</span>
            </div>
          }

          @if (phase() === 'edit') {
            <p class="sru-subtitle">Change team, uniform, or position &middot; Delete a registration</p>
            @for (player of players(); track player.registrationId) {
              <div class="sru-player-row">
                <div class="sru-player-name">
                  <i class="bi bi-person-fill me-1"></i>
                  {{ player.firstName }} {{ player.lastName }}
                </div>
                <div class="sru-field-grid">
                  <div class="field-row">
                    <label class="field-label">Uniform #</label>
                    <input class="field-input" type="text"
                           [ngModel]="edits()[player.registrationId]?.uniformNo ?? ''"
                           (ngModelChange)="updateEdit(player.registrationId, 'uniformNo', $event)">
                  </div>
                  <div class="field-row">
                    <label class="field-label">Position</label>
                    <select class="field-select"
                            [ngModel]="edits()[player.registrationId]?.position ?? ''"
                            (ngModelChange)="updateEdit(player.registrationId, 'position', $event)">
                      <option value="">&mdash; Select &mdash;</option>
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
                      @for (group of groupTeamsByClub(player.availableTeams); track group.clubName) {
                        <optgroup [label]="group.clubName">
                          @for (team of group.teams; track team.teamId) {
                            <option [value]="team.teamId">{{ formatTeamLabel(team) }}</option>
                          }
                        </optgroup>
                      }
                    </select>
                  </div>
                </div>
                <div class="sru-row-actions">
                  <button class="btn btn-primary btn-sm"
                          [disabled]="!isDirty(player.registrationId) || saving()"
                          (click)="savePlayer(player)">
                    @if (saving() && savingId() === player.registrationId) {
                      <span class="spinner-border spinner-border-sm me-1"></span>Saving
                    } @else {
                      <i class="bi bi-check-lg me-1"></i>Save
                    }
                  </button>
                  <button class="btn btn-outline-danger btn-sm"
                          [disabled]="saving()"
                          (click)="confirmDelete(player)"
                          title="Delete registration">
                    <i class="bi bi-trash3"></i>
                  </button>
                </div>
              </div>
            }
          }

        </div>

        <div class="modal-footer">
          <button type="button" class="btn btn-outline-secondary btn-sm" (click)="close()">Close</button>
        </div>

      </div>
    </tsic-dialog>

    @if (deleteTarget()) {
      <confirm-dialog
        title="Delete Registration"
        [message]="'Delete the registration for ' + deleteTarget()!.firstName + ' ' + deleteTarget()!.lastName + '? This cannot be undone. You will need to re-register.'"
        confirmLabel="Delete Registration"
        confirmVariant="danger"
        (confirmed)="executeDelete()"
        (cancelled)="deleteTarget.set(null)">
      </confirm-dialog>
    }
    `,
    styles: [`
      .sru-modal {
        display: flex;
        flex-direction: column;
      }
      .sru-body {
        overflow-y: auto;
      }
      .sru-subtitle {
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
        margin-bottom: var(--space-3);
      }
      .sru-loading {
        text-align: center;
        padding: var(--space-6);
        color: var(--brand-text-muted);
      }
      .sru-empty {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-6) var(--space-3);
        text-align: center;
        color: var(--brand-text-muted);
      }
      .sru-empty i {
        font-size: var(--font-size-3xl);
        color: var(--neutral-300);
      }
      .sru-player-row {
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        padding: var(--space-2) var(--space-3);
        margin-bottom: var(--space-2);
        background: var(--brand-surface);
      }
      .sru-player-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        margin-bottom: var(--space-2);
      }
      .sru-field-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-1) var(--space-2);
      }
      .field-row {
        display: flex;
        flex-direction: column;
        gap: 1px;
      }
      .field-row--wide {
        grid-column: 1 / -1;
      }
      .sru-row-actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--space-2);
        margin-top: var(--space-2);
        padding-top: var(--space-2);
        border-top: 1px dashed var(--border-color);
      }
      @media (max-width: 575.98px) {
        .sru-field-grid { grid-template-columns: 1fr; }
        .field-row--wide { grid-column: auto; }
      }
    `]
})
export class SelfRosterUpdateModalComponent {
    readonly modalService = inject(SelfRosterUpdateModalService);
    private readonly http = inject(HttpClient);
    private readonly auth = inject(AuthService);
    private readonly toast = inject(ToastService);

    readonly phase = signal<Phase>('login');
    readonly loggingIn = signal(false);
    readonly loginError = signal<string | null>(null);
    readonly saving = signal(false);
    readonly savingId = signal<string | null>(null);
    readonly players = signal<SelfRosterPlayerDto[]>([]);
    readonly deleteTarget = signal<SelfRosterPlayerDto | null>(null);
    readonly edits = signal<Record<string, PlayerEditState>>({});
    private snapshots: Record<string, PlayerEditState> = {};

    username = '';
    password = '';

    constructor() {
        // Always start fresh: logout any prior session
        this.auth.logoutLocal();
    }

    close(): void {
        this.modalService.close();
    }

    submitLogin(): void {
        if (!this.username || !this.password) return;
        this.loggingIn.set(true);
        this.loginError.set(null);

        this.auth.login({ username: this.username, password: this.password }).subscribe({
            next: () => {
                this.loggingIn.set(false);
                this.password = '';
                this.phase.set('loading');
                this.loadPlayers();
            },
            error: (err) => {
                this.loggingIn.set(false);
                this.loginError.set(err?.error?.message ?? 'Invalid username or password.');
            }
        });
    }

    isDirty(regId: string): boolean {
        const current = this.edits()[regId];
        const snap = this.snapshots[regId];
        if (!current || !snap) return false;
        return current.uniformNo !== snap.uniformNo
            || current.position !== snap.position
            || current.teamId !== snap.teamId;
    }

    /** Group teams by club name for <optgroup> rendering. */
    groupTeamsByClub(teams: readonly SelfRosterTeamOptionDto[] | undefined): { clubName: string; teams: SelfRosterTeamOptionDto[] }[] {
        if (!teams?.length) return [];
        const map = new Map<string, SelfRosterTeamOptionDto[]>();
        for (const team of teams) {
            const key = team.clubName || '(No Club)';
            const list = map.get(key) ?? [];
            list.push(team);
            map.set(key, list);
        }
        return Array.from(map.entries()).map(([clubName, teams]) => ({ clubName, teams }));
    }

    /** Format team label as "Club:Agegroup:Division:Team (n/max)" matching legacy convention. */
    formatTeamLabel(team: SelfRosterTeamOptionDto): string {
        const parts = [team.clubName, team.agegroupName, team.divisionName, team.teamName].filter(p => !!p);
        const label = parts.join(':');
        return team.maxCount > 0 ? `${label} (${team.currentCount}/${team.maxCount})` : label;
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
                    this.snapshots[player.registrationId] = { ...edit };
                    const team = player.availableTeams?.find(t => t.teamId === edit.teamId);
                    const updated = this.players().map(p =>
                        p.registrationId === player.registrationId
                            ? { ...p, teamName: team?.teamName ?? p.teamName, uniformNo: edit.uniformNo, position: edit.position, teamId: edit.teamId }
                            : p
                    );
                    this.players.set(updated);
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
                    const remaining = this.players().filter(p => p.registrationId !== player.registrationId);
                    this.players.set(remaining);
                    delete this.snapshots[player.registrationId];
                    const currentEdits = { ...this.edits() };
                    delete currentEdits[player.registrationId];
                    this.edits.set(currentEdits);
                    this.toast.show(`Registration for ${player.firstName} ${player.lastName} deleted.`, 'success', 3000);
                    if (remaining.length === 0) this.phase.set('empty');
                },
                error: (err) => {
                    this.saving.set(false);
                    this.toast.show(err?.error?.message ?? 'Failed to delete registration.', 'danger', 5000);
                }
            });
    }

    private loadPlayers(): void {
        const jobPath = this.modalService.jobPath();
        this.http.get<SelfRosterPlayerDto[]>(`${this.apiBase()}/self-roster-update/${encodeURIComponent(jobPath)}/players`)
            .subscribe({
                next: (data) => {
                    this.players.set(data);
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
                    this.phase.set(data.length === 0 ? 'empty' : 'edit');
                },
                error: (err) => {
                    this.toast.show(err?.error?.message ?? 'Failed to load registrations.', 'danger', 5000);
                    this.phase.set('empty');
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
