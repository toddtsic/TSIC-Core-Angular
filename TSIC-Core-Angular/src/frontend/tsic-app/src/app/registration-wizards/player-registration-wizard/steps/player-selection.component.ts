import { Component, EventEmitter, Output, inject, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { FormsModule } from '@angular/forms';
import { environment } from '../../../../environments/environment';

@Component({
  selector: 'app-rw-player-selection',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatProgressSpinnerModule, MatChipsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Select Players</h5>
      </div>
      <div class="card-body position-relative">
        <!-- Temporary debug panel showing raw GetFamilyPlayers response (dev-only) -->
        @if (showDebug() && state.debugFamilyPlayersResp()) {
          <mat-card appearance="outlined" class="mb-3" role="region" aria-label="Family players raw response">
            <div class="d-flex justify-content-between align-items-start mb-2">
              <strong class="me-2">Debug: Raw GetFamilyPlayers Response
                <mat-chip-set class="ms-2"><mat-chip>dev only</mat-chip></mat-chip-set>
              </strong>
              <button type="button" mat-stroked-button (click)="state.debugFamilyPlayersResp.set(null)">Hide</button>
            </div>
            <pre class="small mb-0" style="max-height:240px; overflow:auto;">
{{ state.debugFamilyPlayersResp() | json }}
            </pre>
          </mat-card>
        }
        <!-- Loading overlay -->
        @if (state.familyPlayersLoading()) {
        <div class="position-absolute top-0 start-0 w-100 h-100 d-flex flex-column align-items-center justify-content-center bg-white bg-opacity-75" style="z-index: 10;">
          <mat-progress-spinner mode="indeterminate" [diameter]="32" class="mb-3" aria-label="Loading Family Players"></mat-progress-spinner>
          <div class="fw-semibold">Loading Family Players...</div>
        </div>
        }
        <p class="text-secondary mb-3">Select the players you wish to register.</p>

        <!-- Fallback to classic *ngIf / *ngFor for widest compatibility -->
        @if (!state.familyPlayersLoading() && state.familyPlayers().length === 0) {
          <mat-card appearance="outlined" role="status">No players found for your family. You can add players in your Family Account.</mat-card>
        } @else {
          <ul class="list-group list-group-flush mb-3" [class.opacity-50]="state.familyPlayersLoading()">
            @for (p of state.familyPlayers(); track p.playerId) {
            <li class="list-group-item d-flex align-items-center justify-content-between">
              <div class="d-flex align-items-center gap-3">
      <input type="checkbox"
        class="form-check-input"
        [checked]="isSelected(p.playerId)"
        [disabled]="state.isPlayerLocked(p.playerId)"
        (change)="!state.isPlayerLocked(p.playerId) && state.togglePlayerSelection(p.playerId)"
        [attr.aria-label]="state.isPlayerLocked(p.playerId) ? 'Already registered' : (isSelected(p.playerId) ? 'Deselect player' : 'Select player')" />
                <div>
                  <div class="fw-semibold">{{ p.firstName }} {{ p.lastName }}</div>
                  <div class="text-secondary small">{{ p.gender || 'Gender N/A' }} • {{ p.dob || 'DOB not on file' }}</div>
                </div>
              </div>
              @if (state.isPlayerLocked(p.playerId)) {
                <mat-chip-set><mat-chip title="Previously registered for this job">Locked</mat-chip></mat-chip-set>
              }
            </li>
            }
          </ul>
        }
      </div>
    </div>
  `
})
export class PlayerSelectionComponent implements OnInit {
  @Output() next = new EventEmitter<void>();
  state = inject(RegistrationWizardService);
  // Prefer Angular environment flag; fallback to hostname heuristics if needed
  showDebug = computed(() => {
    try {
      if (environment && typeof environment.production === 'boolean') {
        return !environment.production;
      }
    } catch { /* ignore */ }
    try {
      const host = globalThis.location?.hostname?.toLowerCase() ?? '';
      return host.startsWith('localhost') || host.startsWith('127.') || host.endsWith('.ngrok-free.app');
    } catch { return false; }
  });

  ngOnInit(): void {
    // One-shot initial load; if jobPath arrives later via routing async,
    // caller should invoke loadFamilyPlayers explicitly.
    const jp = this.state.jobPath();
    if (jp && this.state.familyPlayers().length === 0) {
      this.state.loadFamilyPlayers(jp);
    }
  }

  isSelected(id: string): boolean {
    try {
      const fam = this.state.familyPlayers();
      const p = fam.find(x => x.playerId === id);
      return !!p && (p.selected || p.registered);
    } catch { return false; }
  }

}
