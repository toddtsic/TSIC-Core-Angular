import { ChangeDetectionStrategy, Component, Input, Output, EventEmitter, OnChanges, HostListener, computed, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { LadtService } from '../services/ladt.service';
import { FeeCardComponent, type ModifierForm } from './fee-card.component';
import type { AgegroupDetailDto, UpdateAgegroupRequest, JobFeeDto, FeeModifierDto } from '../../../../core/api';
import { AGEGROUP_COLORS } from '../../../scheduling/shared/utils/scheduling-helpers';
import { JobService } from '../../../../infrastructure/services/job.service';

const PLAYER_ROLE = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
const CLUBREP_ROLE = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';
const JOB_TYPE_TOURNAMENT = 2;

@Component({
  selector: 'app-agegroup-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, FeeCardComponent],
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
        <!-- ── Settings (identity + config) ── -->
        <div class="section-card settings-card">
          <div class="section-card-header">
            <i class="bi bi-gear"></i> Settings
          </div>
          <div class="d-flex align-items-end gap-2 mb-2">
            <div class="flex-grow-1">
              <label class="fee-label">Name</label>
              <input class="form-control form-control-sm" [(ngModel)]="form.agegroupName" name="agegroupName">
            </div>
            <div style="min-width: 90px;">
              <label class="fee-label">Gender</label>
              <select class="form-select form-select-sm" [(ngModel)]="form.gender" name="gender">
                <option [ngValue]="null">Any</option>
                <option value="M">Male</option>
                <option value="F">Female</option>
                <option value="C">Co-Ed</option>
              </select>
            </div>
            <div class="color-picker-wrapper" (click)="$event.stopPropagation()">
              <button type="button" class="color-trigger" (click)="colorDropdownOpen.set(!colorDropdownOpen())">
                @if (form.color) {
                  <span class="color-dot-lg" [style.background]="form.color"></span>
                } @else {
                  <span class="color-dot-lg" style="background: transparent; border: 2px dashed var(--bs-border-color);"></span>
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
            <div>
              <label class="fee-label">Max Teams</label>
              <input class="form-control form-control-sm text-center" type="number"
                     [(ngModel)]="form.maxTeams" name="maxTeams" style="width: 60px;">
            </div>
          </div>
          <div class="settings-grid">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bAllowSelfRostering" name="bAllowSelfRostering">
              <label class="form-check-label">Self Rostering</label>
            </div>
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bChampionsByDivision" name="bChampionsByDivision">
              <label class="form-check-label">Champs by Division</label>
            </div>
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bAllowApiRosterAccess" name="bAllowApiRosterAccess">
              <label class="form-check-label">API Roster Access</label>
            </div>
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideStandings" name="bHideStandings">
              <label class="form-check-label">Hide Standings</label>
            </div>
          </div>
        </div>

        @if (isTournament()) {
          <app-fee-card header="Club Rep / Team Fees" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [(deposit)]="feeForm.clubRepDeposit"
            [(balanceDue)]="feeForm.clubRepBalanceDue" [modifiers]="clubRepModifiers" />
          <app-fee-card header="Player Fees" headerIcon="bi-person" variant="player"
            namePrefix="player" [(deposit)]="feeForm.playerDeposit"
            [(balanceDue)]="feeForm.playerBalanceDue" [modifiers]="playerModifiers"
            placeholder="Optional" />
        } @else {
          <app-fee-card header="Player Fees" headerIcon="bi-person" variant="player"
            namePrefix="player" [(deposit)]="feeForm.playerDeposit"
            [(balanceDue)]="feeForm.playerBalanceDue" [modifiers]="playerModifiers"
            placeholder="Optional" />
          <app-fee-card header="Club Rep / Team Fees" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [(deposit)]="feeForm.clubRepDeposit"
            [(balanceDue)]="feeForm.clubRepBalanceDue" [modifiers]="clubRepModifiers" />
        }

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

    /* Section cards */
    .section-card {
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-sm);
      padding: var(--space-3);
      margin-bottom: var(--space-3);
    }
    .settings-card {
      background: var(--bs-tertiary-bg);
      border-color: var(--bs-border-color);
      box-shadow: var(--shadow-sm);
    }
    .settings-card .section-card-header { color: var(--bs-secondary-color); }

    .section-card-header {
      font-size: 0.75rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--bs-secondary-color);
      margin-bottom: var(--space-2);
      display: flex;
      align-items: center;
      gap: var(--space-1);
    }
    .fee-label {
      font-size: 0.75rem;
      color: var(--bs-secondary-color);
      margin-bottom: 2px;
      display: block;
    }

    /* Settings grid */
    .settings-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--space-2);
      font-size: 0.85rem;
    }

    /* Color picker */
    .color-picker-wrapper { position: relative; }
    .color-trigger {
      display: flex; align-items: center; justify-content: center;
      width: 32px; height: 32px; padding: 0; border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-sm); background: var(--bs-body-bg); cursor: pointer;
    }
    .color-trigger:hover { border-color: var(--bs-primary); }
    .color-dot-lg {
      display: block; width: 20px; height: 20px; border-radius: 50%;
      border: 1px solid var(--bs-border-color);
    }
    .color-dot {
      display: inline-block; width: 14px; height: 14px; border-radius: 50%;
      border: 1px solid var(--bs-border-color); vertical-align: middle; flex-shrink: 0;
    }
    .color-dropdown {
      position: absolute; z-index: 1050; top: 100%; right: 0; width: 160px;
      max-height: 240px; overflow-y: auto; background: var(--bs-body-bg);
      border: 1px solid var(--bs-border-color); border-radius: var(--bs-border-radius);
      box-shadow: var(--shadow-lg); margin-top: 2px;
    }
    .color-option {
      display: flex; align-items: center; gap: var(--space-1);
      padding: var(--space-1) var(--space-2); cursor: pointer; font-size: 0.82rem;
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
  private readonly jobService = inject(JobService);

  readonly isTournament = computed(() => this.jobService.currentJob()?.jobTypeId === JOB_TYPE_TOURNAMENT);

  agegroup = signal<AgegroupDetailDto | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);
  isError = signal(false);
  showDeleteConfirm = signal(false);
  colorDropdownOpen = signal(false);

  colorOptions = AGEGROUP_COLORS;
  form: any = {};

  feeForm = {
    playerDeposit: null as number | null,
    playerBalanceDue: null as number | null,
    clubRepDeposit: null as number | null,
    clubRepBalanceDue: null as number | null
  };

  playerModifiers: ModifierForm[] = [];
  clubRepModifiers: ModifierForm[] = [];

  private originalFees = { ...this.feeForm };
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

    forkJoin({
      detail: this.ladtService.getAgegroup(this.agegroupId),
      fees: this.ladtService.getAgegroupFees(this.agegroupId)
    }).subscribe({
      next: ({ detail, fees }) => {
        this.agegroup.set(detail);
        this.form = { ...detail };
        if (this.form.color) this.form.color = this.form.color.toUpperCase();
        this.populateFeeForm(fees);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  private populateFeeForm(fees: JobFeeDto[]): void {
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

    this.playerModifiers = (playerFee?.modifiers ?? []).map(m => this.toModifierForm(m));
    this.clubRepModifiers = (clubRepFee?.modifiers ?? []).map(m => this.toModifierForm(m));
  }

  private toModifierForm(m: FeeModifierDto): ModifierForm {
    return {
      feeModifierId: m.feeModifierId,
      modifierType: m.modifierType,
      amount: m.amount,
      startDate: m.startDate ? String(m.startDate).substring(0, 10) : null,
      endDate: m.endDate ? String(m.endDate).substring(0, 10) : null
    };
  }

  private toModifierDtos(mods: ModifierForm[]): FeeModifierDto[] {
    return mods
      .filter(m => m.amount != null && m.amount > 0)
      .map(m => ({
        feeModifierId: m.feeModifierId,
        modifierType: m.modifierType,
        amount: m.amount!,
        startDate: m.startDate || null,
        endDate: m.endDate || null
      }));
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

    const saves: Observable<any>[] = [
      this.ladtService.updateAgegroup(this.agegroupId, request)
    ];

    // Save player fee row + modifiers
    if (this.feeForm.playerDeposit != null || this.feeForm.playerBalanceDue != null
        || this.playerModifiers.length > 0) {
      saves.push(this.ladtService.saveFee({
        roleId: PLAYER_ROLE,
        agegroupId: this.agegroupId,
        deposit: this.feeForm.playerDeposit,
        balanceDue: this.feeForm.playerBalanceDue,
        modifiers: this.toModifierDtos(this.playerModifiers)
      }));
    } else if (this.playerFeeId) {
      saves.push(this.ladtService.deleteFee(this.playerFeeId));
    }

    // Save club rep fee row + modifiers
    if (this.feeForm.clubRepDeposit != null || this.feeForm.clubRepBalanceDue != null
        || this.clubRepModifiers.length > 0) {
      saves.push(this.ladtService.saveFee({
        roleId: CLUBREP_ROLE,
        agegroupId: this.agegroupId,
        deposit: this.feeForm.clubRepDeposit,
        balanceDue: this.feeForm.clubRepBalanceDue,
        modifiers: this.toModifierDtos(this.clubRepModifiers)
      }));
    } else if (this.clubRepFeeId) {
      saves.push(this.ladtService.deleteFee(this.clubRepFeeId));
    }

    forkJoin(saves).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.isError.set(false);
        this.saveMessage.set('Age group saved successfully.');
        this.originalFees = { ...this.feeForm };
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
