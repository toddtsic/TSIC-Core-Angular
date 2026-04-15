import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LadtService } from '../services/ladt.service';
import type { CloneTeamRequest, TeamDetailDto } from '../../../../core/api';

@Component({
  selector: 'app-clone-team-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="backdrop" (click)="cancel()"></div>
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="clone-dialog-title">
      <div class="dialog-header">
        <h5 id="clone-dialog-title" class="mb-0">
          <i class="bi bi-copy me-2"></i>Clone Team
        </h5>
        <button type="button" class="btn-close" (click)="cancel()" aria-label="Close"></button>
      </div>

      <div class="dialog-body">
        <div class="mb-3">
          <label class="field-label">New Team Name</label>
          <input class="form-control form-control-sm"
                 [(ngModel)]="teamName"
                 [ngModelOptions]="{standalone: true}"
                 (keydown.enter)="clone()"
                 placeholder="Team name (required)"
                 autofocus>
          <div class="wizard-tip">Must differ from the source team's name.</div>
        </div>

        @if (hasClubRep) {
          <div class="toggle-row">
            <div class="form-check">
              <input class="form-check-input" type="checkbox" id="copyClubLinkage"
                     [(ngModel)]="copyClubLinkage"
                     [ngModelOptions]="{standalone: true}"
                     (ngModelChange)="onClubLinkageChange()">
              <label class="form-check-label" for="copyClubLinkage">Copy club linkage</label>
            </div>
            <div class="wizard-tip">
              Assigns the clone to {{ clubName || 'the same club rep' }} and creates a matching
              club-team entry. When on, fees must be copied so the club rep's balance is correct.
            </div>
          </div>
        }

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="copyFees"
                   [(ngModel)]="copyFees"
                   [ngModelOptions]="{standalone: true}"
                   [disabled]="copyFeesLocked()">
            <label class="form-check-label" for="copyFees">Copy fees</label>
          </div>
          <div class="wizard-tip">
            Player fees, club rep fees, and any fee modifiers attached to the source team.
            @if (copyFeesLocked()) {
              <strong>Required when copying club linkage.</strong>
            }
          </div>
        </div>

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="copyEligibility"
                   [(ngModel)]="copyEligibility"
                   [ngModelOptions]="{standalone: true}">
            <label class="form-check-label" for="copyEligibility">Copy eligibility rules</label>
          </div>
          <div class="wizard-tip">DOB range, grad year range, school grade range, and gender restriction.</div>
        </div>

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="copyRosterSettings"
                   [(ngModel)]="copyRosterSettings"
                   [ngModelOptions]="{standalone: true}">
            <label class="form-check-label" for="copyRosterSettings">Copy roster settings</label>
          </div>
          <div class="wizard-tip">Max roster size, self-rostering flag, hide-roster flag.</div>
        </div>

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="copyDates"
                   [(ngModel)]="copyDates"
                   [ngModelOptions]="{standalone: true}">
            <label class="form-check-label" for="copyDates">Copy dates</label>
          </div>
          <div class="wizard-tip">Start, End, Effective, and Expires dates. Often reset per clone.</div>
        </div>

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="copyVisualIdentity"
                   [(ngModel)]="copyVisualIdentity"
                   [ngModelOptions]="{standalone: true}">
            <label class="form-check-label" for="copyVisualIdentity">Copy visual identity</label>
          </div>
          <div class="wizard-tip">Team color and level of play.</div>
        </div>

        @if (errorMessage()) {
          <div class="alert alert-danger alert-sm mb-0 mt-3">
            <i class="bi bi-exclamation-triangle me-1"></i>{{ errorMessage() }}
          </div>
        }
      </div>

      <div class="dialog-footer">
        <button type="button" class="btn btn-sm btn-outline-secondary" (click)="cancel()" [disabled]="isSaving()">
          Cancel
        </button>
        <button type="button" class="btn btn-sm btn-primary" (click)="clone()"
                [disabled]="!canClone()">
          @if (isSaving()) {
            <span class="spinner-border spinner-border-sm me-1"></span>
          } @else {
            <i class="bi bi-copy me-1"></i>
          }
          Clone team
        </button>
      </div>
    </div>
  `,
  styles: [`
    :host { display: contents; }

    .backdrop {
      position: fixed; inset: 0; background: rgba(0, 0, 0, 0.4); z-index: 1060;
    }
    .dialog {
      position: fixed; top: 50%; left: 50%; transform: translate(-50%, -50%);
      width: 480px; max-width: 92vw; max-height: 90vh;
      background: var(--bs-body-bg);
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-md);
      box-shadow: var(--shadow-xl);
      z-index: 1061;
      display: flex; flex-direction: column;
    }
    .dialog-header {
      display: flex; align-items: center; justify-content: space-between;
      padding: var(--space-3) var(--space-4);
      border-bottom: 1px solid var(--bs-border-color);
    }
    .dialog-body {
      padding: var(--space-3) var(--space-4);
      overflow-y: auto;
      flex: 1;
    }
    .dialog-footer {
      display: flex; justify-content: flex-end; gap: var(--space-2);
      padding: var(--space-3) var(--space-4);
      border-top: 1px solid var(--bs-border-color);
    }
    .toggle-row {
      padding: var(--space-2) 0;
      border-bottom: 1px dashed var(--bs-border-color);

      &:last-of-type { border-bottom: none; }
    }
    .wizard-tip {
      font-size: var(--font-size-xs);
      color: var(--bs-secondary-color);
      margin-top: 4px;
      margin-left: 1.5rem;
      line-height: 1.35;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CloneTeamDialogComponent implements OnInit {
  @Input({ required: true }) sourceTeamId!: string;
  @Input({ required: true }) sourceTeamName!: string;
  @Input() hasClubRep = false;
  @Input() clubName: string | null = null;

  @Output() cancelled = new EventEmitter<void>();
  @Output() cloned = new EventEmitter<TeamDetailDto>();

  private readonly ladtService = inject(LadtService);

  teamName = '';
  copyClubLinkage = false;
  copyFees = true;
  copyEligibility = true;
  copyRosterSettings = true;
  copyDates = true;
  copyVisualIdentity = true;

  isSaving = signal(false);
  errorMessage = signal<string | null>(null);

  // Fees toggle is locked (forced on) when club linkage is being copied.
  copyFeesLocked = computed(() => this.hasClubRep && this.copyClubLinkage);

  ngOnInit(): void {
    this.teamName = `${this.sourceTeamName} (Copy)`;
    this.copyClubLinkage = this.hasClubRep;
  }

  onClubLinkageChange(): void {
    if (this.copyClubLinkage) this.copyFees = true;
  }

  canClone(): boolean {
    const name = this.teamName.trim();
    return !!name && name !== this.sourceTeamName && !this.isSaving();
  }

  cancel(): void {
    if (this.isSaving()) return;
    this.cancelled.emit();
  }

  clone(): void {
    if (!this.canClone()) return;

    const request: CloneTeamRequest = {
      teamName: this.teamName.trim(),
      addToClubLibrary: this.hasClubRep && this.copyClubLinkage,
      copyFees: this.copyFeesLocked() ? true : this.copyFees,
      copyEligibility: this.copyEligibility,
      copyRosterSettings: this.copyRosterSettings,
      copyDates: this.copyDates,
      copyVisualIdentity: this.copyVisualIdentity
    };

    this.isSaving.set(true);
    this.errorMessage.set(null);

    this.ladtService.cloneTeam(this.sourceTeamId, request).subscribe({
      next: (clone) => {
        this.isSaving.set(false);
        this.cloned.emit(clone);
      },
      error: (err) => {
        this.isSaving.set(false);
        this.errorMessage.set(err?.error?.message || 'Failed to clone team.');
      }
    });
  }
}
