import { ChangeDetectionStrategy, Component, Input, Output, EventEmitter, OnChanges, HostListener, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { LadtService } from '../services/ladt.service';
import type { AgegroupDetailDto, UpdateAgegroupRequest, JobFeeDto, SaveJobFeeRequest } from '../../../../core/api';
import { AGEGROUP_COLORS } from '../../../scheduling/shared/utils/scheduling-helpers';

/** Role constants matching backend RoleConstants */
const PLAYER_ROLE = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
const CLUBREP_ROLE = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';

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
        @if (hasFeesChanged()) {
          <button class="btn btn-sm btn-outline-warning" (click)="pushFees()" [disabled]="isSaving()"
                  title="Fees have been modified — push updated fees to all players in this age group">
            <i class="bi bi-currency-dollar me-1"></i>Push Fees to Players
          </button>
        }
        <button class="btn btn-sm btn-outline-danger" (click)="confirmDelete()"
                [disabled]="isSaving() || !canDelete"
                [title]="!canDelete ? 'Remove all teams before deleting this age group' : 'Delete this age group'">
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
          <div class="col-md-4">
            <label class="form-label">Name</label>
            <input class="form-control" [(ngModel)]="form.agegroupName" name="agegroupName">
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
            <div class="color-picker-wrapper" (click)="$event.stopPropagation()">
              <button type="button" class="form-select text-start" (click)="colorDropdownOpen.set(!colorDropdownOpen())">
                @if (form.color) {
                  <span class="color-dot" [style.background]="form.color"></span>
                  {{ getColorName(form.color) }}
                } @else {
                  None
                }
              </button>
              @if (colorDropdownOpen()) {
                <div class="color-dropdown">
                  <div class="color-option" (click)="selectColor(null)">
                    <span class="color-dot" style="background: transparent; border: 1px dashed var(--bs-border-color);"></span>
                    None
                  </div>
                  @for (c of colorOptions; track c.value) {
                    <div class="color-option" [class.active]="form.color === c.value" (click)="selectColor(c.value)">
                      <span class="color-dot" [style.background]="c.value"></span>
                      {{ c.name }}
                    </div>
                  }
                </div>
              }
            </div>
          </div>
          <div class="col-md-2">
            <label class="form-label">Sort Order</label>
            <input class="form-control" type="number" [(ngModel)]="form.sortAge" name="sortAge">
          </div>
        </div>

        <h6 class="section-label mt-4">Player Fees</h6>
        <div class="row g-3">
          <div class="col-md-4">
            <label class="form-label">Deposit</label>
            <input class="form-control" type="number" step="0.01"
                   [(ngModel)]="feeForm.playerDeposit" name="playerDeposit"
                   placeholder="Optional — if blank, full amount due">
          </div>
          <div class="col-md-4">
            <label class="form-label">Balance Due</label>
            <input class="form-control" type="number" step="0.01"
                   [(ngModel)]="feeForm.playerBalanceDue" name="playerBalanceDue">
          </div>
        </div>

        <h6 class="section-label mt-4">Club Rep / Team Fees</h6>
        <div class="row g-3">
          <div class="col-md-4">
            <label class="form-label">Deposit</label>
            <input class="form-control" type="number" step="0.01"
                   [(ngModel)]="feeForm.clubRepDeposit" name="clubRepDeposit">
          </div>
          <div class="col-md-4">
            <label class="form-label">Balance Due</label>
            <input class="form-control" type="number" step="0.01"
                   [(ngModel)]="feeForm.clubRepBalanceDue" name="clubRepBalanceDue">
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
    .color-picker-wrapper { position: relative; }
    .color-picker-wrapper .form-select {
      display: flex; align-items: center; gap: var(--space-1); cursor: pointer;
    }
    .color-dot {
      display: inline-block; width: 14px; height: 14px; border-radius: 50%;
      border: 1px solid var(--bs-border-color); vertical-align: middle; flex-shrink: 0;
    }
    .color-dropdown {
      position: absolute; z-index: 1050; top: 100%; left: 0; right: 0;
      max-height: 240px; overflow-y: auto; background: var(--bs-body-bg);
      border: 1px solid var(--bs-border-color); border-radius: var(--bs-border-radius);
      box-shadow: 0 4px 12px rgba(0,0,0,.15); margin-top: 2px;
    }
    .color-option {
      display: flex; align-items: center; gap: var(--space-1);
      padding: var(--space-1) var(--space-2); cursor: pointer; font-size: 0.875rem;
    }
    .color-option:hover { background: var(--bs-tertiary-bg); }
    .color-option.active { background: var(--bs-primary-bg-subtle); font-weight: 600; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AgegroupDetailComponent implements OnChanges {
  @Input({ required: true }) agegroupId!: string;
  @Input() canDelete = true;
  @Input() playerCount = 0;
  @Output() saved = new EventEmitter<void>();
  @Output() deleted = new EventEmitter<void>();

  private readonly ladtService = inject(LadtService);

  agegroup = signal<AgegroupDetailDto | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);
  isError = signal(false);
  showDeleteConfirm = signal(false);
  colorDropdownOpen = signal(false);

  colorOptions = AGEGROUP_COLORS;
  form: any = {};

  // Fee form — separate from agegroup entity form
  feeForm = {
    playerDeposit: null as number | null,
    playerBalanceDue: null as number | null,
    clubRepDeposit: null as number | null,
    clubRepBalanceDue: null as number | null
  };

  // Track original fee values for change detection
  private originalFees = { ...this.feeForm };
  // Track fee row IDs for updates
  private playerFeeId: string | null = null;
  private clubRepFeeId: string | null = null;

  @HostListener('document:click')
  onDocumentClick(): void {
    this.colorDropdownOpen.set(false);
  }

  ngOnChanges(): void {
    this.loadDetail();
  }

  private loadDetail(): void {
    this.isLoading.set(true);
    this.saveMessage.set(null);
    this.showDeleteConfirm.set(false);

    // Load agegroup detail + fees in parallel
    forkJoin({
      detail: this.ladtService.getAgegroup(this.agegroupId),
      fees: this.ladtService.getAgegroupFees(this.agegroupId)
    }).subscribe({
      next: ({ detail, fees }) => {
        this.agegroup.set(detail);
        this.form = { ...detail };
        if (this.form.color) this.form.color = this.form.color.toUpperCase();

        // Populate fee form from fee rows
        this.populateFeeForm(fees);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  private populateFeeForm(fees: JobFeeDto[]): void {
    // Find agegroup-level fee rows (TeamId = null)
    const playerFee = fees.find(f => f.roleId === PLAYER_ROLE && !f.teamId);
    const clubRepFee = fees.find(f => f.roleId === CLUBREP_ROLE && !f.teamId);

    this.playerFeeId = playerFee?.jobFeeId ?? null;
    this.clubRepFeeId = clubRepFee?.jobFeeId ?? null;

    this.feeForm = {
      playerDeposit: playerFee?.deposit ?? null,
      playerBalanceDue: playerFee?.balanceDue ?? null,
      clubRepDeposit: clubRepFee?.deposit ?? null,
      clubRepBalanceDue: clubRepFee?.balanceDue ?? null
    };
    this.originalFees = { ...this.feeForm };
  }

  save(): void {
    this.isSaving.set(true);
    this.saveMessage.set(null);

    const request: UpdateAgegroupRequest = {
      agegroupName: this.form.agegroupName,
      color: this.form.color,
      gender: this.form.gender,
      dobMin: this.form.dobMin,
      dobMax: this.form.dobMax,
      gradYearMin: this.form.gradYearMin,
      gradYearMax: this.form.gradYearMax,
      schoolGradeMin: this.form.schoolGradeMin,
      schoolGradeMax: this.form.schoolGradeMax,
      maxTeams: this.form.maxTeams,
      maxTeamsPerClub: this.form.maxTeamsPerClub,
      bAllowSelfRostering: this.form.bAllowSelfRostering,
      bChampionsByDivision: this.form.bChampionsByDivision,
      bAllowApiRosterAccess: this.form.bAllowApiRosterAccess,
      bHideStandings: this.form.bHideStandings,
      sortAge: this.form.sortAge
    };

    // Save agegroup entity + fee rows
    const saves: Observable<any>[] = [
      this.ladtService.updateAgegroup(this.agegroupId, request)
    ];

    // Save player fee row if any value is set
    if (this.feeForm.playerDeposit != null || this.feeForm.playerBalanceDue != null) {
      saves.push(this.ladtService.saveFee({
        roleId: PLAYER_ROLE,
        agegroupId: this.agegroupId,
        deposit: this.feeForm.playerDeposit,
        balanceDue: this.feeForm.playerBalanceDue
      }));
    } else if (this.playerFeeId) {
      // Both cleared — delete the row
      saves.push(this.ladtService.deleteFee(this.playerFeeId));
    }

    // Save club rep fee row if any value is set
    if (this.feeForm.clubRepDeposit != null || this.feeForm.clubRepBalanceDue != null) {
      saves.push(this.ladtService.saveFee({
        roleId: CLUBREP_ROLE,
        agegroupId: this.agegroupId,
        deposit: this.feeForm.clubRepDeposit,
        balanceDue: this.feeForm.clubRepBalanceDue
      }));
    } else if (this.clubRepFeeId) {
      saves.push(this.ladtService.deleteFee(this.clubRepFeeId));
    }

    forkJoin(saves).subscribe({
      next: (results) => {
        // First result is always the agegroup update
        const updated = results[0] as AgegroupDetailDto;
        this.agegroup.set(updated);
        this.form = { ...updated };
        this.originalFees = { ...this.feeForm };
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

  hasFeesChanged(): boolean {
    if (this.playerCount === 0) return false;
    return this.feeForm.playerDeposit !== this.originalFees.playerDeposit ||
           this.feeForm.playerBalanceDue !== this.originalFees.playerBalanceDue ||
           this.feeForm.clubRepDeposit !== this.originalFees.clubRepDeposit ||
           this.feeForm.clubRepBalanceDue !== this.originalFees.clubRepBalanceDue;
  }

  getColorName(hex: string): string {
    return this.colorOptions.find(c => c.value === hex)?.name ?? hex;
  }

  selectColor(value: string | null): void {
    this.form.color = value;
    this.colorDropdownOpen.set(false);
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
        this.saveMessage.set(`Updated fees for ${count} registration(s).`);
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to update player fees.');
      }
    });
  }
}
