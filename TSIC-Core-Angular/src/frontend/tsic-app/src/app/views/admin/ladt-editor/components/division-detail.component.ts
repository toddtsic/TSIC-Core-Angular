import { Component, Input, Output, EventEmitter, OnChanges, signal, inject } from '@angular/core';
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
      <button class="btn btn-sm btn-outline-danger" (click)="confirmDelete()" [disabled]="isSaving()">
        <i class="bi bi-trash me-1"></i>Delete
      </button>
    </div>

    @if (isLoading()) {
      <div class="text-center py-4">
        <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
      </div>
    } @else if (division()) {
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
        <div class="row g-3">
          <div class="col-md-8">
            <label class="form-label">Division Name</label>
            <input class="form-control" [(ngModel)]="form.divName" name="divName">
          </div>
          <div class="col-md-4">
            <label class="form-label">Max Round # to Show</label>
            <input class="form-control" type="number" [(ngModel)]="form.maxRoundNumberToShow" name="maxRoundNumberToShow">
          </div>
        </div>

        <div class="d-flex gap-2 mt-4">
          <button type="submit" class="btn btn-primary" [disabled]="isSaving()">
            @if (isSaving()) {
              <span class="spinner-border spinner-border-sm me-1"></span>
            }
            Save Changes
          </button>
        </div>

        @if (saveMessage()) {
          <div class="alert mt-3 py-2" [class.alert-success]="!isError()" [class.alert-danger]="isError()" role="alert">
            <i class="bi me-1" [class.bi-check-circle]="!isError()" [class.bi-exclamation-triangle]="isError()"></i>
            {{ saveMessage() }}
          </div>
        }
      </form>
    }
  `,
  styles: [`
    :host { display: block; }
    .detail-header { margin-bottom: var(--space-4); }
  `]
})
export class DivisionDetailComponent implements OnChanges {
  @Input({ required: true }) divisionId!: string;
  @Output() saved = new EventEmitter<void>();
  @Output() deleted = new EventEmitter<void>();

  private readonly ladtService = inject(LadtService);

  division = signal<DivisionDetailDto | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);
  isError = signal(false);
  showDeleteConfirm = signal(false);

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
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  save(): void {
    this.isSaving.set(true);
    this.saveMessage.set(null);

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
