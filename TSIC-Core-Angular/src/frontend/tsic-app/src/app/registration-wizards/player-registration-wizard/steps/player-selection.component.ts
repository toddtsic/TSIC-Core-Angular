import { Component, EventEmitter, Output, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-rw-player-selection',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Select Players</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Select the players you wish to register.</p>

        <!-- Fallback to classic *ngIf / *ngFor for widest compatibility -->
        <ng-container *ngIf="state.familyPlayers().length === 0; else playersList">
          <div class="alert alert-info">No players found for your family. You can add players in your Family Account.</div>
        </ng-container>
        <ng-template #playersList>
          <ul class="list-group list-group-flush mb-3">
            <li class="list-group-item d-flex align-items-center justify-content-between" *ngFor="let p of state.familyPlayers(); trackBy: trackPlayer">
              <div class="d-flex align-items-center gap-3">
                <input type="checkbox"
                       class="form-check-input"
                       [checked]="isSelected(p.playerId)"
                       [disabled]="p.registered"
                       (change)="state.togglePlayerSelection(p)"
                       [attr.aria-label]="p.registered ? 'Already registered' : 'Select player'" />
                <div>
                  <div class="fw-semibold">{{ p.firstName }} {{ p.lastName }}</div>
                  <div class="text-secondary small">{{ p.gender || 'Gender N/A' }} • {{ p.dob || 'DOB not on file' }}</div>
                </div>
              </div>
              <span *ngIf="p.registered" class="badge bg-secondary" title="You cannot remove a previous registration">Locked</span>
            </li>
          </ul>
        </ng-template>

        <div class="rw-bottom-nav d-flex gap-2">
          <button type="button" class="btn btn-primary" [disabled]="state.selectedPlayers().length === 0" (click)="next.emit()">Continue</button>
        </div>

        <!-- Debug hook for verifying players signal (remove later) -->
        <div *ngIf="debugEnabled" class="small text-muted mt-3">
          Debug: players={{ state.familyPlayers().length }} selected={{ state.selectedPlayers().length }} activeFamilyUser={{ state.activeFamilyUser()?.displayName || 'none' }}
        </div>
      </div>
    </div>
  `
})
export class PlayerSelectionComponent implements OnInit {
  @Output() next = new EventEmitter<void>();
  state = inject(RegistrationWizardService);
  // Temporary debug flag (could wire to query param later)
  debugEnabled = false;

  ngOnInit(): void {
    // Load players when we have jobPath and an active family user
    const jobPath = this.state.jobPath();
    const fam = this.state.activeFamilyUser();
    if (jobPath && fam?.familyUserId) {
      this.state.loadFamilyPlayers(jobPath, fam.familyUserId);
    }
    // Enable debug if players already present (helps confirm signal flow visually)
    if (this.state.familyPlayers().length > 0) {
      this.debugEnabled = true;
    }
  }

  isSelected(id: string): boolean {
    try {
      const list = this.state.selectedPlayers();
      return Array.isArray(list) && list.some(sp => sp.userId === id);
    } catch { return false; }
  }

  trackPlayer = (_: number, p: { playerId: string }) => p.playerId;
}
