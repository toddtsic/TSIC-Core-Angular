import { ChangeDetectionStrategy, Component, Input, Output, EventEmitter, OnChanges, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LadtService } from '../services/ladt.service';
import type { DivisionDetailDto, UpdateDivisionRequest } from '../../../../core/api';

@Component({
  selector: 'app-division-detail',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="detail-header d-flex align-items-center justify-content-between">
      <div class="d-flex align-items-center gap-2">
        <i class="bi bi-grid-3x3-gap text-warning"></i>
        <h5 class="mb-0">Division Details</h5>
      </div>
      <button class="btn btn-sm btn-outline-danger" (click)="confirmDelete()"
              [disabled]="isSaving() || !canDelete"
              [title]="isUnassigned() ? 'The Unassigned division cannot be deleted' : !canDelete ? 'Remove all teams before deleting this division' : 'Delete this division'">
        <i class="bi bi-trash me-1"></i>Delete
      </button>
    </div>

    @if (isLoading()) {
      <div class="text-center py-4">
        <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
      </div>
    } @else if (division()) {
      @if (isUnassigned()) {
        <div class="alert alert-info py-2 d-flex align-items-center" role="alert">
          <i class="bi bi-lock me-2"></i>
          The 'Unassigned' division is required and cannot be renamed or deleted.
        </div>
      }

      @if (showDeleteConfirm()) {
        <div class="alert alert-danger d-flex align-items-center justify-content-between" role="alert">
          <span><i class="bi bi-exclamation-triangle me-2"></i>Delete this division? This cannot be undone.</span>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-secondary" (click)="showDeleteConfirm.set(false)">Cancel</button>
            <button class="btn btn-sm btn-danger" (click)="doDelete()">Delete</button>
          </div>
        </div>
      }

      <form (ngSubmit)="save()">
        <!-- ── Settings ── -->
        <div class="section-card settings-card">
          <div class="section-card-header">
            <i class="bi bi-gear"></i> Settings
          </div>
          <div class="d-flex align-items-end gap-2">
            <div class="flex-grow-1">
              <label class="fee-label">Division Name</label>
              <input class="form-control form-control-sm" [(ngModel)]="form.divName" name="divName"
                     [disabled]="isUnassigned()">
            </div>
            <div style="width: 80px;">
              <label class="fee-label">Max Round#</label>
              <input class="form-control form-control-sm text-center" type="number"
                     [(ngModel)]="form.maxRoundNumberToShow" name="maxRoundNumberToShow">
            </div>
          </div>
        </div>

        <!-- ── Save ── -->
        <div class="d-flex align-items-center gap-3 mt-3">
          <button type="submit" class="btn btn-sm btn-primary px-4" [disabled]="isSaving()">
            @if (isSaving()) {
              <span class="spinner-border spinner-border-sm me-1"></span>
            }
            Save
          </button>
          @if (saveMessage()) {
            <span class="small" [class.text-success]="!isError()" [class.text-danger]="isError()">
              <i class="bi me-1" [class.bi-check-circle]="!isError()" [class.bi-exclamation-triangle]="isError()"></i>
              {{ saveMessage() }}
            </span>
          }
        </div>
      </form>
    }
  `,
  styles: [`
    :host { display: block; }
    .detail-header { margin-bottom: var(--space-3); }
    .section-card {
      border: 1px solid var(--bs-border-color); border-radius: var(--radius-sm);
      padding: var(--space-3); margin-bottom: var(--space-3);
    }
    .section-card-header {
      font-size: 0.75rem; font-weight: 700; text-transform: uppercase;
      letter-spacing: 0.04em; color: var(--bs-secondary-color);
      margin-bottom: var(--space-2); display: flex; align-items: center; gap: var(--space-1);
    }
    .settings-card { background: var(--bs-tertiary-bg); box-shadow: var(--shadow-sm); }
    .fee-label { font-size: 0.75rem; color: var(--bs-secondary-color); margin-bottom: 2px; display: block; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DivisionDetailComponent implements OnChanges {
  @Input({ required: true }) divisionId!: string;
  @Input() siblingNames: string[] = [];
  @Input() canDelete = true;
  @Output() saved = new EventEmitter<void>();
  @Output() deleted = new EventEmitter<void>();

  private readonly ladtService = inject(LadtService);

  division = signal<DivisionDetailDto | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);
  isError = signal(false);
  showDeleteConfirm = signal(false);
  isUnassigned = signal(false);

  form: any = {};

  ngOnChanges(): void {
    this.loadDetail();
  }

  private loadDetail(): void {
    this.isLoading.set(true);
    this.saveMessage.set(null);
    this.showDeleteConfirm.set(false);

    this.ladtService.getDivision(this.divisionId).subscribe({
      next: (detail) => {
        this.division.set(detail);
        this.form = { ...detail };
        this.isUnassigned.set(detail.divName?.toUpperCase() === 'UNASSIGNED');
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  save(): void {
    this.saveMessage.set(null);

    // Client-side duplicate name check
    const newName = (this.form.divName ?? '').trim();
    const currentName = this.division()?.divName ?? '';
    if (newName.toUpperCase() !== currentName.toUpperCase()) {
      const duplicate = this.siblingNames.some(
        n => n.toUpperCase() === newName.toUpperCase()
      );
      if (duplicate) {
        this.isError.set(true);
        this.saveMessage.set(`A division named '${newName}' already exists in this age group.`);
        return;
      }
    }

    this.isSaving.set(true);

    const request: UpdateDivisionRequest = {
      divName: this.form.divName,
      maxRoundNumberToShow: this.form.maxRoundNumberToShow
    };

    this.ladtService.updateDivision(this.divisionId, request).subscribe({
      next: (updated) => {
        this.division.set(updated);
        this.form = { ...updated };
        this.isSaving.set(false);
        this.isError.set(false);
        this.saveMessage.set('Division saved successfully.');
        this.saved.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to save division.');
      }
    });
  }

  confirmDelete(): void {
    this.showDeleteConfirm.set(true);
  }

  doDelete(): void {
    this.isSaving.set(true);
    this.ladtService.deleteDivision(this.divisionId).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.deleted.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to delete division.');
        this.showDeleteConfirm.set(false);
      }
    });
  }
}
