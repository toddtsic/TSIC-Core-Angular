import { ChangeDetectionStrategy, Component, Input, Output, EventEmitter, OnChanges, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { LadtService } from '../services/ladt.service';
import { FeeCardComponent, type ModifierForm } from './fee-card.component';
import { ConfirmDialogComponent } from '../../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { TeamDetailDto, UpdateTeamRequest, ClubRegistrationDto, MoveTeamToClubRequest, JobFeeDto } from '../../../../core/api';

const PLAYER_ROLE = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
const CLUBREP_ROLE = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';

@Component({
  selector: 'app-team-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, FeeCardComponent, ConfirmDialogComponent],
  template: `
    <div class="detail-header d-flex align-items-center justify-content-between">
      <div class="d-flex align-items-center gap-2">
        <i class="bi bi-person-badge text-info"></i>
        <h5 class="mb-0">Team Details</h5>
        @if (team()?.playerCount) {
          <span class="badge bg-info-subtle text-info-emphasis">{{ team()!.playerCount | number }} players</span>
        }
      </div>
      <div class="d-flex gap-2">
        <button class="btn btn-sm btn-outline-secondary" (click)="openCloneDialog()" [disabled]="isSaving()" title="Clone team">
          <i class="bi bi-copy me-1"></i>Clone
        </button>
        <button class="btn btn-sm btn-outline-danger" (click)="confirmDrop()" [disabled]="isSaving()">
          <i class="bi bi-box-arrow-down me-1"></i>Drop
        </button>
        <div style="position: relative;">
          <button class="btn btn-sm btn-outline-secondary" (click)="moreOpen.set(!moreOpen())" title="More actions">
            <i class="bi bi-three-dots-vertical"></i>
          </button>
          @if (moreOpen()) {
            <ul class="dropdown-menu dropdown-menu-end show" style="position: absolute; right: 0; top: 100%;">
              @if (team()?.clubRepRegistrationId) {
                <li><button class="dropdown-item" (click)="confirmChangeClub(); moreOpen.set(false)">
                  <i class="bi bi-arrow-left-right me-2"></i>Change Club
                </button></li>
              }
            </ul>
          }
        </div>
      </div>
    </div>

    @if (isLoading()) {
      <div class="text-center py-4">
        <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
      </div>
    } @else if (team()) {
      @if (showDropConfirm()) {
        <div class="alert alert-danger d-flex align-items-center justify-content-between" role="alert">
          <span>
            <i class="bi bi-exclamation-triangle me-2"></i>
            Drop this team? It will be moved to Dropped Teams and deactivated.
          </span>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-secondary" (click)="showDropConfirm.set(false)">Cancel</button>
            <button class="btn btn-sm btn-danger" (click)="doDrop()">Drop</button>
          </div>
        </div>
      }

      @if (showCloneDialog()) {
        <div class="alert alert-secondary" role="alert">
          <h6 class="alert-heading mb-2">
            <i class="bi bi-copy me-1"></i>Clone Team
          </h6>
          <div class="mb-2">
            <label class="form-label small mb-1">New Team Name</label>
            <input class="form-control form-control-sm"
                   [ngModel]="cloneName()"
                   (ngModelChange)="cloneName.set($event)"
                   [ngModelOptions]="{standalone: true}">
          </div>
          @if (team()?.clubRepRegistrationId) {
            <div class="form-check mb-2">
              <input class="form-check-input" type="checkbox" id="cloneAddToClub"
                     [ngModel]="cloneAddToClub()"
                     (ngModelChange)="cloneAddToClub.set($event)"
                     [ngModelOptions]="{standalone: true}">
              <label class="form-check-label small" for="cloneAddToClub">
                Add to {{ team()?.clubName }}'s team library
              </label>
            </div>
          }
          <div class="d-flex gap-2">
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="showCloneDialog.set(false)">Cancel</button>
            <button type="button" class="btn btn-sm btn-primary" (click)="doClone()"
                    [disabled]="!cloneName().trim() || isSaving()">
              @if (isSaving()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              }
              Clone
            </button>
          </div>
        </div>
      }

      @if (showChangeClub()) {
        <div class="alert alert-info" role="alert">
          <h6 class="alert-heading mb-2">
            <i class="bi bi-arrow-left-right me-1"></i>Move to Different Club
          </h6>
          <p class="small mb-2">Current club: <strong>{{ team()?.clubName }}</strong></p>

          <select class="form-select form-select-sm mb-2"
                  [ngModel]="selectedTargetRegistrationId()"
                  (ngModelChange)="selectedTargetRegistrationId.set($event)"
                  [ngModelOptions]="{standalone: true}">
            <option value="">Select target club...</option>
            @for (club of clubRegistrations(); track club.registrationId) {
              <option [value]="club.registrationId">{{ club.clubName }}</option>
            }
          </select>

          <div class="d-flex gap-2">
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="cancelChangeClub()">Cancel</button>
            <button type="button" class="btn btn-sm btn-primary" (click)="doChangeClub()"
                    [disabled]="!selectedTargetRegistrationId() || isMoving()">
              @if (isMoving()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              }
              Move
            </button>
          </div>
        </div>
      }

      <form (ngSubmit)="save()">
        <!-- ── Settings ── -->
        <div class="section-card settings-card">
          <div class="section-card-header">
            <i class="bi bi-gear"></i> Settings
          </div>
          <div class="d-flex align-items-end gap-2 mb-2">
            <div class="flex-grow-1">
              <label class="fee-label">Team Name</label>
              <input class="form-control form-control-sm" [(ngModel)]="form.teamName" name="teamName">
            </div>
            <div class="form-check form-switch" style="padding-bottom: 4px;">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.active" name="active">
              <label class="form-check-label">Active</label>
            </div>
          </div>
          <div class="d-flex align-items-center gap-2 mb-2">
            <label class="fee-label">Max Roster</label>
            <input class="form-control form-control-sm" type="number" [(ngModel)]="form.maxCount" name="maxCount" style="width: 80px;">
          </div>
          <div class="settings-grid">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bAllowSelfRostering" name="bAllowSelfRostering">
              <label class="form-check-label">Self Rostering</label>
            </div>
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideRoster" name="bHideRoster">
              <label class="form-check-label">Hide Roster</label>
            </div>
          </div>
        </div>

        <!-- ── Player Fee Override ── -->
        <app-fee-card header="Player Fee Override" headerIcon="bi-person" variant="player"
          namePrefix="player" [(deposit)]="feeForm.playerDeposit"
          [(balanceDue)]="feeForm.playerBalanceDue" [modifiers]="playerModifiers"
          hintText="Leave blank to use the agegroup default." placeholder="Agegroup default" />

        <!-- ── Club Rep Fee Override ── -->
        <app-fee-card header="Club Rep Fee Override" headerIcon="bi-shield" variant="clubrep"
          namePrefix="clubRep" [(deposit)]="feeForm.clubRepDeposit"
          [(balanceDue)]="feeForm.clubRepBalanceDue" [modifiers]="clubRepModifiers"
          hintText="Leave blank to use the agegroup default." placeholder="Agegroup default" />

        <!-- ── Dates ── -->
        <div class="section-card">
          <div class="section-card-header">
            <i class="bi bi-calendar3"></i> Dates
          </div>
          <div class="row g-2">
            <div class="col-6">
              <label class="fee-label">Start Date</label>
              <input class="form-control form-control-sm" type="date" [(ngModel)]="form.startdate" name="startdate">
            </div>
            <div class="col-6">
              <label class="fee-label">End Date</label>
              <input class="form-control form-control-sm" type="date" [(ngModel)]="form.enddate" name="enddate">
            </div>
            <div class="col-6">
              <label class="fee-label">Effective As Of</label>
              <input class="form-control form-control-sm" type="date" [(ngModel)]="form.effectiveasofdate" name="effectiveasofdate">
            </div>
            <div class="col-6">
              <label class="fee-label">Expire On</label>
              <input class="form-control form-control-sm" type="date" [(ngModel)]="form.expireondate" name="expireondate">
            </div>
          </div>
        </div>

        <!-- ── Eligibility ── -->
        <div class="section-card">
          <div class="section-card-header">
            <i class="bi bi-funnel"></i> Eligibility
          </div>
          <div class="d-flex gap-2">
            <div style="min-width: 100px;">
              <label class="fee-label">Gender</label>
              <select class="form-select form-select-sm" [(ngModel)]="form.gender" name="gender">
                <option [ngValue]="null">Any</option>
                <option value="M">Male</option>
                <option value="F">Female</option>
                <option value="C">Co-Ed</option>
              </select>
            </div>
            <div style="width: 80px;">
              <label class="fee-label">Level of Play</label>
              <input class="form-control form-control-sm" [(ngModel)]="form.levelOfPlay" name="levelOfPlay">
            </div>
          </div>
        </div>

        <!-- ── Notes ── -->
        <div class="section-card">
          <div class="section-card-header">
            <i class="bi bi-chat-text"></i> Notes
          </div>
          <div class="row g-2">
            <div class="col-6">
              <label class="fee-label">Requests</label>
              <textarea class="form-control form-control-sm" rows="2" [(ngModel)]="form.requests" name="requests"></textarea>
            </div>
            <div class="col-6">
              <label class="fee-label">Team Comments</label>
              <textarea class="form-control form-control-sm" rows="2" [(ngModel)]="form.teamComments" name="teamComments"></textarea>
            </div>
            <div class="col-12">
              <label class="fee-label">Keyword Pairs</label>
              <input class="form-control form-control-sm" [(ngModel)]="form.keywordPairs" name="keywordPairs">
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

    @if (showChangeClubWarning()) {
      <confirm-dialog
        title="Change Club — Are You Sure?"
        message="This operation will reassign team ownership to a different club. It affects club rep financials, schedule data, and team assignments. Only proceed if you are certain this is correct."
        confirmLabel="I Understand, Proceed"
        confirmVariant="warning"
        (confirmed)="onChangeClubWarningConfirmed()"
        (cancelled)="showChangeClubWarning.set(false)"
      />
    }
  `,
  styles: [`
    :host { display: block; }
    .detail-header { margin-bottom: var(--space-3); }
    .section-card {
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-sm);
      padding: var(--space-3);
      margin-bottom: var(--space-3);
    }
    .section-card-header {
      font-size: 0.75rem; font-weight: 700; text-transform: uppercase;
      letter-spacing: 0.04em; color: var(--bs-secondary-color);
      margin-bottom: var(--space-2); display: flex; align-items: center; gap: var(--space-1);
    }
    .settings-card { background: var(--bs-tertiary-bg); box-shadow: var(--shadow-sm); }
    .settings-card .section-card-header { color: var(--bs-secondary-color); }
    .fee-label { font-size: 0.75rem; color: var(--bs-secondary-color); margin-bottom: 2px; display: block; }
    .settings-grid { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-2); font-size: 0.85rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamDetailComponent implements OnChanges {
  @Input({ required: true }) teamId!: string;
  @Output() saved = new EventEmitter<void>();
  @Output() cloned = new EventEmitter<void>();
  @Output() clubChanged = new EventEmitter<void>();

  private readonly ladtService = inject(LadtService);

  team = signal<TeamDetailDto | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);
  isError = signal(false);
  showDropConfirm = signal(false);
  showChangeClub = signal(false);
  clubRegistrations = signal<ClubRegistrationDto[]>([]);
  selectedTargetRegistrationId = signal('');
  isMoving = signal(false);
  moreOpen = signal(false);
  showChangeClubWarning = signal(false);
  showCloneDialog = signal(false);
  cloneName = signal('');
  cloneAddToClub = signal(false);

  form: any = {};

  feeForm = {
    playerDeposit: null as number | null,
    playerBalanceDue: null as number | null,
    clubRepDeposit: null as number | null,
    clubRepBalanceDue: null as number | null
  };
  playerModifiers: ModifierForm[] = [];
  clubRepModifiers: ModifierForm[] = [];
  private playerFeeId: string | null = null;
  private clubRepFeeId: string | null = null;

  ngOnChanges(): void {
    this.loadDetail();
  }

  private loadDetail(): void {
    this.isLoading.set(true);
    this.saveMessage.set(null);
    this.showDropConfirm.set(false);
    this.showChangeClub.set(false);

    this.ladtService.getTeam(this.teamId).subscribe({
      next: (detail) => {
        this.team.set(detail);
        this.form = { ...detail };
        for (const key of ['startdate', 'enddate', 'effectiveasofdate', 'expireondate']) {
          if (this.form[key]) {
            this.form[key] = String(this.form[key]).substring(0, 10);
          }
        }

        // Load fees for this team's agegroup (includes team-level overrides)
        this.ladtService.getAgegroupFees(detail.agegroupId).subscribe({
          next: (fees) => {
            const playerFee = fees.find((f: JobFeeDto) => f.roleId === PLAYER_ROLE && f.teamId === this.teamId);
            const clubRepFee = fees.find((f: JobFeeDto) => f.roleId === CLUBREP_ROLE && f.teamId === this.teamId);
            this.playerFeeId = playerFee?.jobFeeId ?? null;
            this.clubRepFeeId = clubRepFee?.jobFeeId ?? null;
            this.feeForm = {
              playerDeposit: playerFee?.deposit ?? null,
              playerBalanceDue: playerFee?.balanceDue ?? null,
              clubRepDeposit: clubRepFee?.deposit ?? null,
              clubRepBalanceDue: clubRepFee?.balanceDue ?? null
            };
            this.playerModifiers = (playerFee?.modifiers ?? []).map((m: any) => ({
              feeModifierId: m.feeModifierId,
              modifierType: m.modifierType,
              amount: m.amount,
              startDate: m.startDate?.substring(0, 10) ?? null,
              endDate: m.endDate?.substring(0, 10) ?? null
            }));
            this.clubRepModifiers = (clubRepFee?.modifiers ?? []).map((m: any) => ({
              feeModifierId: m.feeModifierId,
              modifierType: m.modifierType,
              amount: m.amount,
              startDate: m.startDate?.substring(0, 10) ?? null,
              endDate: m.endDate?.substring(0, 10) ?? null
            }));
            this.isLoading.set(false);
          },
          error: () => this.isLoading.set(false)
        });
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

    const saves: Observable<any>[] = [
      this.ladtService.updateTeam(this.teamId, request)
    ];

    const detail = this.team();
    const agegroupId = detail?.agegroupId;

    // Save player fee override (team-level) if any value set or modifiers exist
    const hasPlayerFee = this.feeForm.playerDeposit != null || this.feeForm.playerBalanceDue != null || this.playerModifiers.length > 0;
    if (agegroupId && hasPlayerFee) {
      saves.push(this.ladtService.saveFee({
        roleId: PLAYER_ROLE,
        agegroupId,
        teamId: this.teamId,
        deposit: this.feeForm.playerDeposit,
        balanceDue: this.feeForm.playerBalanceDue,
        modifiers: this.playerModifiers
      }));
    } else if (this.playerFeeId) {
      saves.push(this.ladtService.deleteFee(this.playerFeeId));
    }

    // Save club rep fee override (team-level) if any value set or modifiers exist
    const hasClubRepFee = this.feeForm.clubRepDeposit != null || this.feeForm.clubRepBalanceDue != null || this.clubRepModifiers.length > 0;
    if (agegroupId && hasClubRepFee) {
      saves.push(this.ladtService.saveFee({
        roleId: CLUBREP_ROLE,
        agegroupId,
        teamId: this.teamId,
        deposit: this.feeForm.clubRepDeposit,
        balanceDue: this.feeForm.clubRepBalanceDue,
        modifiers: this.clubRepModifiers
      }));
    } else if (this.clubRepFeeId) {
      saves.push(this.ladtService.deleteFee(this.clubRepFeeId));
    }

    forkJoin(saves).subscribe({
      next: (results) => {
        const updated = results[0] as TeamDetailDto;
        this.team.set(updated);
        this.form = { ...updated };
        for (const key of ['startdate', 'enddate', 'effectiveasofdate', 'expireondate']) {
          if (this.form[key]) {
            this.form[key] = String(this.form[key]).substring(0, 10);
          }
        }
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

  confirmDrop(): void {
    this.showDropConfirm.set(true);
  }

  doDrop(): void {
    this.isSaving.set(true);
    this.ladtService.dropTeam(this.teamId).subscribe({
      next: (result) => {
        this.isSaving.set(false);
        this.showDropConfirm.set(false);
        this.isError.set(false);
        this.saveMessage.set(result.message);
        this.loadDetail();
        this.saved.emit(); // refresh tree to show moved/inactive state
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to drop team.');
        this.showDropConfirm.set(false);
      }
    });
  }

  openCloneDialog(): void {
    this.cloneName.set(`${this.team()?.teamName ?? ''} (Copy)`);
    this.cloneAddToClub.set(!!this.team()?.clubRepRegistrationId);
    this.showCloneDialog.set(true);
  }

  doClone(): void {
    const name = this.cloneName()?.trim();
    if (!name) return;

    this.isSaving.set(true);
    this.ladtService.cloneTeam(this.teamId, { teamName: name, addToClubLibrary: this.cloneAddToClub() }).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.isError.set(false);
        this.showCloneDialog.set(false);
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

  confirmChangeClub(): void {
    this.showChangeClubWarning.set(true);
  }

  onChangeClubWarningConfirmed(): void {
    this.showChangeClubWarning.set(false);
    this.saveMessage.set(null);
    this.selectedTargetRegistrationId.set('');
    this.ladtService.getClubRegistrationsForJob().subscribe({
      next: (clubs) => {
        // Filter out the current team's club
        const currentRegId = this.team()?.clubRepRegistrationId;
        this.clubRegistrations.set(clubs.filter(c => c.registrationId !== currentRegId));
        this.showChangeClub.set(true);
      },
      error: (err) => {
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to load club list.');
      }
    });
  }

  doChangeClub(): void {
    const targetId = this.selectedTargetRegistrationId();
    if (!targetId) return;

    this.isMoving.set(true);
    this.saveMessage.set(null);

    const request: MoveTeamToClubRequest = {
      targetRegistrationId: targetId,
      moveAllFromClub: false
    };

    this.ladtService.moveTeamToClub(this.teamId, request).subscribe({
      next: (result) => {
        this.isMoving.set(false);
        this.showChangeClub.set(false);
        this.isError.set(false);
        this.saveMessage.set(result.message);
        this.loadDetail();
        this.clubChanged.emit();
      },
      error: (err) => {
        this.isMoving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to move team.');
      }
    });
  }

  cancelChangeClub(): void {
    this.showChangeClub.set(false);
    this.selectedTargetRegistrationId.set('');
    this.clubRegistrations.set([]);
  }
}
