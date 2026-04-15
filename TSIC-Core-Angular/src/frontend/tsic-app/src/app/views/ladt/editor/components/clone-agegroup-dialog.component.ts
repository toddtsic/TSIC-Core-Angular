import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LadtService } from '../services/ladt.service';
import type { CloneAgegroupRequest, AgegroupDetailDto } from '../../../../core/api';

@Component({
  selector: 'app-clone-agegroup-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="backdrop" (click)="cancel()"></div>
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="clone-ag-dialog-title">
      <div class="dialog-header">
        <h5 id="clone-ag-dialog-title" class="mb-0">
          <i class="bi bi-copy me-2"></i>Clone Age Group
        </h5>
        <button type="button" class="btn-close" (click)="cancel()" aria-label="Close"></button>
      </div>

      <div class="dialog-body">
        <div class="mb-3">
          <label class="field-label">New Age Group Name</label>
          <input class="form-control form-control-sm"
                 [(ngModel)]="agegroupName"
                 [ngModelOptions]="{standalone: true}"
                 (keydown.enter)="clone()"
                 placeholder="Age group name (required)"
                 autofocus>
          <div class="wizard-tip">Must differ from the source age group's name.</div>
        </div>

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="cag-eligibility"
                   [(ngModel)]="copyEligibility"
                   [ngModelOptions]="{standalone: true}">
            <label class="form-check-label" for="cag-eligibility">Copy eligibility rules</label>
          </div>
          <div class="wizard-tip">DOB range, grad year range, school grade range, gender, max teams, max teams per club.</div>
        </div>

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="cag-roster"
                   [(ngModel)]="copyRosterSettings"
                   [ngModelOptions]="{standalone: true}">
            <label class="form-check-label" for="cag-roster">Copy roster settings</label>
          </div>
          <div class="wizard-tip">Self-rostering, champions by division, hide standings, API roster access.</div>
        </div>

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="cag-visual"
                   [(ngModel)]="copyVisualIdentity"
                   [ngModelOptions]="{standalone: true}">
            <label class="form-check-label" for="cag-visual">Copy visual identity</label>
          </div>
          <div class="wizard-tip">Age group color.</div>
        </div>

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="cag-fees"
                   [(ngModel)]="copyFees"
                   [ngModelOptions]="{standalone: true}">
            <label class="form-check-label" for="cag-fees">Copy fees</label>
          </div>
          <div class="wizard-tip">Agegroup-scoped fee rows and modifiers, plus late-fee and discount windows.</div>
        </div>

        <div class="toggle-row">
          <div class="form-check">
            <input class="form-check-input" type="checkbox" id="cag-divisions"
                   [(ngModel)]="copyDivisions"
                   [ngModelOptions]="{standalone: true}">
            <label class="form-check-label" for="cag-divisions">Copy divisions</label>
          </div>
          <div class="wizard-tip">Division shells only (no teams). Teams are never cloned with an age group.</div>
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
          Clone age group
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
export class CloneAgegroupDialogComponent implements OnInit {
  @Input({ required: true }) sourceAgegroupId!: string;
  @Input({ required: true }) sourceAgegroupName!: string;

  @Output() cancelled = new EventEmitter<void>();
  @Output() cloned = new EventEmitter<AgegroupDetailDto>();

  private readonly ladtService = inject(LadtService);

  agegroupName = '';
  copyEligibility = true;
  copyRosterSettings = true;
  copyVisualIdentity = true;
  copyFees = true;
  copyDivisions = true;

  isSaving = signal(false);
  errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.agegroupName = `${this.sourceAgegroupName} (Copy)`;
  }

  canClone(): boolean {
    const name = this.agegroupName.trim();
    return !!name && name !== this.sourceAgegroupName && !this.isSaving();
  }

  cancel(): void {
    if (this.isSaving()) return;
    this.cancelled.emit();
  }

  clone(): void {
    if (!this.canClone()) return;

    const request: CloneAgegroupRequest = {
      agegroupName: this.agegroupName.trim(),
      copyEligibility: this.copyEligibility,
      copyRosterSettings: this.copyRosterSettings,
      copyVisualIdentity: this.copyVisualIdentity,
      copyFees: this.copyFees,
      copyDivisions: this.copyDivisions
    };

    this.isSaving.set(true);
    this.errorMessage.set(null);

    this.ladtService.cloneAgegroup(this.sourceAgegroupId, request).subscribe({
      next: (clone) => {
        this.isSaving.set(false);
        this.cloned.emit(clone);
      },
      error: (err) => {
        this.isSaving.set(false);
        this.errorMessage.set(err?.error?.message || 'Failed to clone age group.');
      }
    });
  }
}
