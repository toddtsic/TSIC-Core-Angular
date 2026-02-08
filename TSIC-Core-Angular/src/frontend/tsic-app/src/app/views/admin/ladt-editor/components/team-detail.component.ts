import { Component, Input, Output, EventEmitter, OnChanges, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LadtService } from '../services/ladt.service';
import type { TeamDetailDto, UpdateTeamRequest } from '../../../../core/api';

@Component({
  selector: 'app-team-detail',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="detail-header d-flex align-items-center justify-content-between">
      <div class="d-flex align-items-center gap-2">
        <i class="bi bi-person-badge text-info"></i>
        <h5 class="mb-0">Team Details</h5>
        @if (team()?.playerCount) {
          <span class="badge bg-info-subtle text-info-emphasis">{{ team()!.playerCount }} players</span>
        }
      </div>
      <div class="d-flex gap-2">
        <button class="btn btn-sm btn-outline-secondary" (click)="clone()" [disabled]="isSaving()" title="Clone team">
          <i class="bi bi-copy me-1"></i>Clone
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
    } @else if (team()) {
      @if (showDeleteConfirm()) {
        <div class="alert alert-danger d-flex align-items-center justify-content-between" role="alert">
          <span>
            <i class="bi bi-exclamation-triangle me-2"></i>
            @if ((team()?.playerCount ?? 0) > 0) {
              Team has {{ team()?.playerCount }} player(s). It will be deactivated instead of deleted.
            } @else {
              Delete this team? This cannot be undone.
            }
          </span>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-secondary" (click)="showDeleteConfirm.set(false)">Cancel</button>
            <button class="btn btn-sm btn-danger" (click)="doDelete()">
              {{ (team()?.playerCount ?? 0) > 0 ? 'Deactivate' : 'Delete' }}
            </button>
          </div>
        </div>
      }

      <form (ngSubmit)="save()">
        <div class="row g-3">
          <div class="col-md-6">
            <label class="form-label">Team Name</label>
            <input class="form-control" [(ngModel)]="form.teamName" name="teamName">
          </div>
          <div class="col-md-3">
            <label class="form-label">Color</label>
            <input class="form-control" [(ngModel)]="form.color" name="color">
          </div>
          <div class="col-md-3">
            <div class="form-check form-switch mt-4">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.active" name="active">
              <label class="form-check-label">Active</label>
            </div>
          </div>
        </div>

        <h6 class="section-label mt-4">Roster</h6>
        <div class="row g-3">
          <div class="col-md-4">
            <label class="form-label">Max Roster Count</label>
            <input class="form-control" type="number" [(ngModel)]="form.maxCount" name="maxCount">
          </div>
          <div class="col-md-4">
            <div class="form-check form-switch mt-4">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bAllowSelfRostering" name="bAllowSelfRostering">
              <label class="form-check-label">Allow Self Rostering</label>
            </div>
          </div>
          <div class="col-md-4">
            <div class="form-check form-switch mt-4">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideRoster" name="bHideRoster">
              <label class="form-check-label">Hide Roster</label>
            </div>
          </div>
          <div class="col-md-6">
            <label class="form-label">Division Requested</label>
            <input class="form-control" [(ngModel)]="form.divisionRequested" name="divisionRequested">
          </div>
          <div class="col-md-6">
            <label class="form-label">Last League Record</label>
            <input class="form-control" [(ngModel)]="form.lastLeagueRecord" name="lastLeagueRecord">
          </div>
        </div>

        <h6 class="section-label mt-4">Fees</h6>
        <div class="row g-3">
          <div class="col-md-4">
            <label class="form-label">Base Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.feeBase" name="feeBase">
          </div>
          <div class="col-md-4">
            <label class="form-label">Per Registrant Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.perRegistrantFee" name="perRegistrantFee">
          </div>
          <div class="col-md-4">
            <label class="form-label">Per Registrant Deposit</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.perRegistrantDeposit" name="perRegistrantDeposit">
          </div>
          <div class="col-md-4">
            <label class="form-label">Discount Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.discountFee" name="discountFee">
          </div>
          <div class="col-md-4">
            <label class="form-label">Late Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.lateFee" name="lateFee">
          </div>
        </div>

        <h6 class="section-label mt-4">Dates</h6>
        <div class="row g-3">
          <div class="col-md-3">
            <label class="form-label">Start Date</label>
            <input class="form-control" type="date" [(ngModel)]="form.startdate" name="startdate">
          </div>
          <div class="col-md-3">
            <label class="form-label">End Date</label>
            <input class="form-control" type="date" [(ngModel)]="form.enddate" name="enddate">
          </div>
          <div class="col-md-3">
            <label class="form-label">Effective As Of</label>
            <input class="form-control" type="date" [(ngModel)]="form.effectiveasofdate" name="effectiveasofdate">
          </div>
          <div class="col-md-3">
            <label class="form-label">Expire On</label>
            <input class="form-control" type="date" [(ngModel)]="form.expireondate" name="expireondate">
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
            <label class="form-label">Gender</label>
            <select class="form-select" [(ngModel)]="form.gender" name="gender">
              <option [ngValue]="null">Any</option>
              <option value="M">Male</option>
              <option value="F">Female</option>
              <option value="C">Co-Ed</option>
            </select>
          </div>
          <div class="col-md-3">
            <label class="form-label">Season</label>
            <input class="form-control" [(ngModel)]="form.season" name="season">
          </div>
          <div class="col-md-3">
            <label class="form-label">Level of Play</label>
            <input class="form-control" [(ngModel)]="form.levelOfPlay" name="levelOfPlay">
          </div>
          <div class="col-md-3">
            <label class="form-label">Year</label>
            <input class="form-control" [(ngModel)]="form.year" name="year">
          </div>
        </div>

        <h6 class="section-label mt-4">Schedule Preferences</h6>
        <div class="row g-3">
          <div class="col-md-4">
            <label class="form-label">Day of Week 1</label>
            <input class="form-control" [(ngModel)]="form.dow" name="dow">
          </div>
          <div class="col-md-4">
            <label class="form-label">Day of Week 2</label>
            <input class="form-control" [(ngModel)]="form.dow2" name="dow2">
          </div>
        </div>

        <h6 class="section-label mt-4">Notes</h6>
        <div class="row g-3">
          <div class="col-md-6">
            <label class="form-label">Requests</label>
            <textarea class="form-control" rows="2" [(ngModel)]="form.requests" name="requests"></textarea>
          </div>
          <div class="col-md-6">
            <label class="form-label">Team Comments</label>
            <textarea class="form-control" rows="2" [(ngModel)]="form.teamComments" name="teamComments"></textarea>
          </div>
          <div class="col-md-12">
            <label class="form-label">Keyword Pairs</label>
            <input class="form-control" [(ngModel)]="form.keywordPairs" name="keywordPairs">
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
export class TeamDetailComponent implements OnChanges {
  @Input({ required: true }) teamId!: string;
  @Output() saved = new EventEmitter<void>();
  @Output() deleted = new EventEmitter<void>();
  @Output() cloned = new EventEmitter<void>();

  private readonly ladtService = inject(LadtService);

  team = signal<TeamDetailDto | null>(null);
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

    this.ladtService.getTeam(this.teamId).subscribe({
      next: (detail) => {
        this.team.set(detail);
        this.form = { ...detail };
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  save(): void {
    this.isSaving.set(true);
    this.saveMessage.set(null);

    const request: UpdateTeamRequest = {
      teamName: this.form.teamName,
      active: this.form.active,
      divisionRequested: this.form.divisionRequested,
      lastLeagueRecord: this.form.lastLeagueRecord,
      color: this.form.color,
      maxCount: this.form.maxCount,
      bAllowSelfRostering: this.form.bAllowSelfRostering,
      bHideRoster: this.form.bHideRoster,
      feeBase: this.form.feeBase,
      perRegistrantFee: this.form.perRegistrantFee,
      perRegistrantDeposit: this.form.perRegistrantDeposit,
      discountFee: this.form.discountFee,
      discountFeeStart: this.form.discountFeeStart,
      discountFeeEnd: this.form.discountFeeEnd,
      lateFee: this.form.lateFee,
      lateFeeStart: this.form.lateFeeStart,
      lateFeeEnd: this.form.lateFeeEnd,
      startdate: this.form.startdate,
      enddate: this.form.enddate,
      effectiveasofdate: this.form.effectiveasofdate,
      expireondate: this.form.expireondate,
      dobMin: this.form.dobMin,
      dobMax: this.form.dobMax,
      gradYearMin: this.form.gradYearMin,
      gradYearMax: this.form.gradYearMax,
      schoolGradeMin: this.form.schoolGradeMin,
      schoolGradeMax: this.form.schoolGradeMax,
      gender: this.form.gender,
      season: this.form.season,
      year: this.form.year,
      dow: this.form.dow,
      dow2: this.form.dow2,
      fieldId1: this.form.fieldId1,
      fieldId2: this.form.fieldId2,
      fieldId3: this.form.fieldId3,
      levelOfPlay: this.form.levelOfPlay,
      requests: this.form.requests,
      keywordPairs: this.form.keywordPairs,
      teamComments: this.form.teamComments
    };

    this.ladtService.updateTeam(this.teamId, request).subscribe({
      next: (updated) => {
        this.team.set(updated);
        this.form = { ...updated };
        this.isSaving.set(false);
        this.isError.set(false);
        this.saveMessage.set('Team saved successfully.');
        this.saved.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to save team.');
      }
    });
  }

  confirmDelete(): void {
    this.showDeleteConfirm.set(true);
  }

  doDelete(): void {
    this.isSaving.set(true);
    this.ladtService.deleteTeam(this.teamId).subscribe({
      next: (result) => {
        this.isSaving.set(false);
        this.showDeleteConfirm.set(false);

        if (result.wasDeactivated) {
          // Soft delete: team still exists but is now inactive.
          // Reload the detail so the form reflects Active = false.
          this.isError.set(false);
          this.saveMessage.set(result.message);
          this.loadDetail();
          this.saved.emit(); // refresh tree to show inactive state
        } else {
          // Hard delete: team is gone, clear selection.
          this.deleted.emit();
        }
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to delete team.');
        this.showDeleteConfirm.set(false);
      }
    });
  }

  clone(): void {
    this.isSaving.set(true);
    this.ladtService.cloneTeam(this.teamId).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.isError.set(false);
        this.saveMessage.set('Team cloned successfully.');
        this.cloned.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to clone team.');
      }
    });
  }
}
