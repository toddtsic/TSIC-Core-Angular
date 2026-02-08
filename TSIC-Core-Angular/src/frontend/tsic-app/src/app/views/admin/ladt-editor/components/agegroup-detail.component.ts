import { Component, Input, Output, EventEmitter, OnChanges, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LadtService } from '../services/ladt.service';
import type { AgegroupDetailDto, UpdateAgegroupRequest } from '../../../../core/api';

@Component({
  selector: 'app-agegroup-detail',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="detail-header d-flex align-items-center justify-content-between">
      <div class="d-flex align-items-center gap-2">
        <i class="bi bi-people text-success"></i>
        <h5 class="mb-0">Age Group Details</h5>
      </div>
      <div class="d-flex gap-2">
        <button class="btn btn-sm btn-outline-secondary" (click)="pushFees()" [disabled]="isSaving()"
                title="Update all team fees to match this age group">
          <i class="bi bi-currency-dollar me-1"></i>Push Fees
        </button>
        <button class="btn btn-sm btn-outline-danger" (click)="confirmDelete()" [disabled]="isSaving()">
          <i class="bi bi-trash me-1"></i>Delete
        </button>
      </div>
    </div>

    @if (isLoading()) {
      <div class="text-center py-4">
        <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
      </div>
    } @else if (agegroup()) {
      <!-- Delete confirmation -->
      @if (showDeleteConfirm()) {
        <div class="alert alert-danger d-flex align-items-center justify-content-between" role="alert">
          <span><i class="bi bi-exclamation-triangle me-2"></i>Delete this age group? This cannot be undone.</span>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-secondary" (click)="showDeleteConfirm.set(false)">Cancel</button>
            <button class="btn btn-sm btn-danger" (click)="doDelete()">Delete</button>
          </div>
        </div>
      }

      <form (ngSubmit)="save()">
        <div class="row g-3">
          <div class="col-md-6">
            <label class="form-label">Name</label>
            <input class="form-control" [(ngModel)]="form.agegroupName" name="agegroupName">
          </div>
          <div class="col-md-3">
            <label class="form-label">Season</label>
            <input class="form-control" [(ngModel)]="form.season" name="season">
          </div>
          <div class="col-md-3">
            <label class="form-label">Gender</label>
            <select class="form-select" [(ngModel)]="form.gender" name="gender">
              <option [ngValue]="null">Any</option>
              <option value="M">Male</option>
              <option value="F">Female</option>
              <option value="C">Co-Ed</option>
            </select>
          </div>
          <div class="col-md-3">
            <label class="form-label">Color</label>
            <input class="form-control" [(ngModel)]="form.color" name="color">
          </div>
          <div class="col-md-3">
            <label class="form-label">Sort Order</label>
            <input class="form-control" type="number" [(ngModel)]="form.sortAge" name="sortAge">
          </div>
        </div>

        <h6 class="section-label mt-4">Eligibility</h6>
        <div class="row g-3">
          <div class="col-md-3">
            <label class="form-label">DOB Min</label>
            <input class="form-control" type="date" [(ngModel)]="form.dobMin" name="dobMin">
          </div>
          <div class="col-md-3">
            <label class="form-label">DOB Max</label>
            <input class="form-control" type="date" [(ngModel)]="form.dobMax" name="dobMax">
          </div>
          <div class="col-md-3">
            <label class="form-label">Grad Year Min</label>
            <input class="form-control" type="number" [(ngModel)]="form.gradYearMin" name="gradYearMin">
          </div>
          <div class="col-md-3">
            <label class="form-label">Grad Year Max</label>
            <input class="form-control" type="number" [(ngModel)]="form.gradYearMax" name="gradYearMax">
          </div>
          <div class="col-md-3">
            <label class="form-label">School Grade Min</label>
            <input class="form-control" type="number" [(ngModel)]="form.schoolGradeMin" name="schoolGradeMin">
          </div>
          <div class="col-md-3">
            <label class="form-label">School Grade Max</label>
            <input class="form-control" type="number" [(ngModel)]="form.schoolGradeMax" name="schoolGradeMax">
          </div>
        </div>

        <h6 class="section-label mt-4">Fees</h6>
        <div class="row g-3">
          <div class="col-md-4">
            <label class="form-label">Team Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.teamFee" name="teamFee">
          </div>
          <div class="col-md-4">
            <label class="form-label">Team Fee Label</label>
            <input class="form-control" [(ngModel)]="form.teamFeeLabel" name="teamFeeLabel">
          </div>
          <div class="col-md-4">
            <label class="form-label">Roster Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.rosterFee" name="rosterFee">
          </div>
          <div class="col-md-4">
            <label class="form-label">Roster Fee Label</label>
            <input class="form-control" [(ngModel)]="form.rosterFeeLabel" name="rosterFeeLabel">
          </div>
          <div class="col-md-4">
            <label class="form-label">Discount Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.discountFee" name="discountFee">
          </div>
          <div class="col-md-4">
            <label class="form-label">Late Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.lateFee" name="lateFee">
          </div>
          <div class="col-md-4">
            <label class="form-label">Player Fee Override</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.playerFeeOverride" name="playerFeeOverride">
          </div>
        </div>

        <h6 class="section-label mt-4">Capacity</h6>
        <div class="row g-3">
          <div class="col-md-4">
            <label class="form-label">Max Teams</label>
            <input class="form-control" type="number" [(ngModel)]="form.maxTeams" name="maxTeams">
          </div>
          <div class="col-md-4">
            <label class="form-label">Max Teams Per Club</label>
            <input class="form-control" type="number" [(ngModel)]="form.maxTeamsPerClub" name="maxTeamsPerClub">
          </div>
        </div>

        <h6 class="section-label mt-4">Options</h6>
        <div class="row g-3">
          <div class="col-md-6">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bAllowSelfRostering" name="bAllowSelfRostering">
              <label class="form-check-label">Allow Self Rostering</label>
            </div>
          </div>
          <div class="col-md-6">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bChampionsByDivision" name="bChampionsByDivision">
              <label class="form-check-label">Champions By Division</label>
            </div>
          </div>
          <div class="col-md-6">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bAllowApiRosterAccess" name="bAllowApiRosterAccess">
              <label class="form-check-label">Allow API Roster Access</label>
            </div>
          </div>
          <div class="col-md-6">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideStandings" name="bHideStandings">
              <label class="form-check-label">Hide Standings</label>
            </div>
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
    .section-label {
      font-size: 0.8rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.03em;
      color: var(--bs-secondary-color);
      border-bottom: 1px solid var(--bs-border-color);
      padding-bottom: var(--space-1);
    }
  `]
})
export class AgegroupDetailComponent implements OnChanges {
  @Input({ required: true }) agegroupId!: string;
  @Output() saved = new EventEmitter<void>();
  @Output() deleted = new EventEmitter<void>();

  private readonly ladtService = inject(LadtService);

  agegroup = signal<AgegroupDetailDto | null>(null);
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

    this.ladtService.getAgegroup(this.agegroupId).subscribe({
      next: (detail) => {
        this.agegroup.set(detail);
        this.form = { ...detail };
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  save(): void {
    this.isSaving.set(true);
    this.saveMessage.set(null);

    const request: UpdateAgegroupRequest = {
      agegroupName: this.form.agegroupName,
      season: this.form.season,
      color: this.form.color,
      gender: this.form.gender,
      dobMin: this.form.dobMin,
      dobMax: this.form.dobMax,
      gradYearMin: this.form.gradYearMin,
      gradYearMax: this.form.gradYearMax,
      schoolGradeMin: this.form.schoolGradeMin,
      schoolGradeMax: this.form.schoolGradeMax,
      teamFee: this.form.teamFee,
      teamFeeLabel: this.form.teamFeeLabel,
      rosterFee: this.form.rosterFee,
      rosterFeeLabel: this.form.rosterFeeLabel,
      discountFee: this.form.discountFee,
      discountFeeStart: this.form.discountFeeStart,
      discountFeeEnd: this.form.discountFeeEnd,
      lateFee: this.form.lateFee,
      lateFeeStart: this.form.lateFeeStart,
      lateFeeEnd: this.form.lateFeeEnd,
      maxTeams: this.form.maxTeams,
      maxTeamsPerClub: this.form.maxTeamsPerClub,
      bAllowSelfRostering: this.form.bAllowSelfRostering,
      bChampionsByDivision: this.form.bChampionsByDivision,
      bAllowApiRosterAccess: this.form.bAllowApiRosterAccess,
      bHideStandings: this.form.bHideStandings,
      playerFeeOverride: this.form.playerFeeOverride,
      sortAge: this.form.sortAge
    };

    this.ladtService.updateAgegroup(this.agegroupId, request).subscribe({
      next: (updated) => {
        this.agegroup.set(updated);
        this.form = { ...updated };
        this.isSaving.set(false);
        this.isError.set(false);
        this.saveMessage.set('Age group saved successfully.');
        this.saved.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to save age group.');
      }
    });
  }

  confirmDelete(): void {
    this.showDeleteConfirm.set(true);
  }

  doDelete(): void {
    this.isSaving.set(true);
    this.ladtService.deleteAgegroup(this.agegroupId).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.deleted.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to delete age group.');
        this.showDeleteConfirm.set(false);
      }
    });
  }

  pushFees(): void {
    this.isSaving.set(true);
    this.ladtService.updatePlayerFeesToAgegroupFees(this.agegroupId).subscribe({
      next: (count) => {
        this.isSaving.set(false);
        this.isError.set(false);
        this.saveMessage.set(`Updated fees for ${count} team(s).`);
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to push fees.');
      }
    });
  }
}
