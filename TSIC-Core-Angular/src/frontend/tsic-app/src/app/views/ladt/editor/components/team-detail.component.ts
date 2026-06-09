import { ChangeDetectionStrategy, Component, OnChanges, computed, signal, inject, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { LadtService } from '../services/ladt.service';
import { FeeRepriceService } from '../services/fee-reprice.service';
import { FeeCardComponent, type ModifierForm } from './fee-card.component';
import { ConfirmDialogComponent } from '../../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import { CloneTeamDialogComponent } from './clone-team-dialog.component';
import { JobService } from '../../../../infrastructure/services/job.service';
import type { TeamDetailDto, UpdateTeamRequest, ClubRegistrationDto, MoveTeamToClubRequest, JobFeeDto } from '../../../../core/api';

const PLAYER_ROLE = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
const CLUBREP_ROLE = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';
const JOB_TYPE_TOURNAMENT = 2;

@Component({
  selector: 'app-team-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, FeeCardComponent, ConfirmDialogComponent, CloneTeamDialogComponent],
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
        @if (team()?.clubRepRegistrationId) {
          <div style="position: relative;">
            <button class="btn btn-sm btn-outline-secondary" (click)="moreOpen.set(!moreOpen())" title="More actions">
              <i class="bi bi-three-dots-vertical"></i>
            </button>
            @if (moreOpen()) {
              <ul class="dropdown-menu dropdown-menu-end show" style="position: absolute; right: 0; top: 100%;">
                <li><button class="dropdown-item" (click)="confirmChangeClub(); moreOpen.set(false)">
                  <i class="bi bi-arrow-left-right me-2"></i>Change Club
                </button></li>
              </ul>
            }
          </div>
        }
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

      @if (showCloneDialog() && team(); as t) {
        <app-clone-team-dialog
          [sourceTeamId]="teamId()"
          [sourceTeamName]="t.teamName ?? ''"
          [hasClubRep]="!!t.clubRepRegistrationId"
          [clubName]="t.clubName ?? null"
          (cancelled)="showCloneDialog.set(false)"
          (cloned)="onCloneSuccess($event)" />
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

        @if (isTournament()) {
          <app-fee-card header="Club Rep Fee Override" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [(deposit)]="feeForm.clubRepDeposit"
            [(balanceDue)]="feeForm.clubRepBalanceDue" [(bFullPaymentRequired)]="feeForm.clubRepPhase"
            [modifiers]="clubRepModifiers"
            hintText="Leave blank to use the agegroup default." placeholder="Agegroup default" />
          <app-fee-card header="Player Fee Override" headerIcon="bi-person" variant="player"
            namePrefix="player" [(deposit)]="feeForm.playerDeposit"
            [(balanceDue)]="feeForm.playerBalanceDue" [(bFullPaymentRequired)]="feeForm.playerPhase"
            [modifiers]="playerModifiers"
            hintText="Leave blank to use the agegroup default." placeholder="Agegroup default" />
        } @else {
          <app-fee-card header="Player Fee Override" headerIcon="bi-person" variant="player"
            namePrefix="player" [(deposit)]="feeForm.playerDeposit"
            [(balanceDue)]="feeForm.playerBalanceDue" [(bFullPaymentRequired)]="feeForm.playerPhase"
            [modifiers]="playerModifiers"
            hintText="Leave blank to use the agegroup default." placeholder="Agegroup default" />
          <app-fee-card header="Club Rep Fee Override" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [(deposit)]="feeForm.clubRepDeposit"
            [(balanceDue)]="feeForm.clubRepBalanceDue" [(bFullPaymentRequired)]="feeForm.clubRepPhase"
            [modifiers]="clubRepModifiers"
            hintText="Leave blank to use the agegroup default." placeholder="Agegroup default" />
        }

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
            <div style="min-width: 120px;">
              <label class="fee-label">Level of Play</label>
              <select class="form-select form-select-sm" [(ngModel)]="form.levelOfPlay" name="levelOfPlay">
                <option [ngValue]="null">—</option>
                <option value="1">1</option>
                <option value="2">2</option>
                <option value="3">3</option>
                <option value="4">4</option>
                <option value="5">5 (strongest)</option>
              </select>
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

    @if (repriceDialog(); as dlg) {
      <confirm-dialog
        [title]="dlg.isPhase ? 'Convert payment phase?' : 'Update existing registrations?'"
        [message]="dlg.message"
        [confirmLabel]="dlg.isPhase ? 'Convert' : 'Update all'"
        [cancelLabel]="dlg.isPhase ? 'Cancel' : 'Future only'"
        confirmVariant="warning"
        (confirmed)="onRepriceConfirm()"
        (cancelled)="onRepriceDismiss()" />
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
  readonly teamId = input.required<string>();
  readonly saved = output<void>();
  readonly cloned = output<string>();
  readonly clubChanged = output<void>();
  readonly dropped = output<void>();

  private readonly ladtService = inject(LadtService);
  private readonly jobService = inject(JobService);
  private readonly feeReprice = inject(FeeRepriceService);

  readonly isTournament = computed(() => this.jobService.currentJob()?.jobTypeId === JOB_TYPE_TOURNAMENT);

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

  form: any = {};

  feeForm = {
    playerDeposit: null as number | null,
    playerBalanceDue: null as number | null,
    playerPhase: null as boolean | null,
    clubRepDeposit: null as number | null,
    clubRepBalanceDue: null as number | null,
    clubRepPhase: null as boolean | null
  };
  playerModifiers: ModifierForm[] = [];
  clubRepModifiers: ModifierForm[] = [];

  // Reprice prompt: null = closed; isPhase drives the confirm/cancel semantics + copy.
  repriceDialog = signal<{ isPhase: boolean; message: string } | null>(null);

  private originalSnapshot = { player: '', clubRep: '' };
  private originalPhase = { player: null as boolean | null, clubRep: null as boolean | null };
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

    this.ladtService.getTeam(this.teamId()).subscribe({
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
            const playerFee = fees.find((f: JobFeeDto) => f.roleId === PLAYER_ROLE && f.teamId === this.teamId());
            const clubRepFee = fees.find((f: JobFeeDto) => f.roleId === CLUBREP_ROLE && f.teamId === this.teamId());
            this.playerFeeId = playerFee?.jobFeeId ?? null;
            this.clubRepFeeId = clubRepFee?.jobFeeId ?? null;
            this.feeForm = {
              playerDeposit: playerFee?.deposit ?? null,
              playerBalanceDue: playerFee?.balanceDue ?? null,
              playerPhase: playerFee?.bFullPaymentRequired ?? null,
              clubRepDeposit: clubRepFee?.deposit ?? null,
              clubRepBalanceDue: clubRepFee?.balanceDue ?? null,
              clubRepPhase: clubRepFee?.bFullPaymentRequired ?? null
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
            this.captureOriginals();
            this.isLoading.set(false);
          },
          error: () => this.isLoading.set(false)
        });
      },
      error: () => this.isLoading.set(false)
    });
  }

  save(): void {
    const playerChanged = this.roleChanged('player');
    const clubRepChanged = this.roleChanged('clubRep');

    if (!playerChanged && !clubRepChanged) {
      this.performSave(false);
      return;
    }

    const phaseFlip = (playerChanged && this.feeForm.playerPhase !== this.originalPhase.player)
                   || (clubRepChanged && this.feeForm.clubRepPhase !== this.originalPhase.clubRep);

    this.isSaving.set(true);
    this.saveMessage.set(null);
    this.feeReprice.getBlastArea(
      { teamId: this.teamId() },
      { player: playerChanged, clubRep: clubRepChanged }
    ).subscribe({
      next: (blast) => {
        if (blast.playerCount + blast.teamCount === 0) {
          this.performSave(false);
          return;
        }
        this.repriceDialog.set({
          isPhase: phaseFlip,
          message: this.feeReprice.buildMessage(blast, this.scopeLabel(), phaseFlip)
        });
        this.isSaving.set(false);
      },
      error: () => this.performSave(false)
    });
  }

  private performSave(repriceExisting: boolean): void {
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
      teamComments: this.form.teamComments
    };

    const saves: Observable<any>[] = [
      this.ladtService.updateTeam(this.teamId(), request)
    ];

    const detail = this.team();
    const agegroupId = detail?.agegroupId;
    const teamId = this.teamId();

    // Save player fee override (team-level) if any value set or modifiers exist
    const hasPlayerFee = this.feeForm.playerDeposit != null || this.feeForm.playerBalanceDue != null
        || this.feeForm.playerPhase != null || this.playerModifiers.length > 0;
    if (agegroupId && hasPlayerFee) {
      saves.push(this.ladtService.saveFee({
        roleId: PLAYER_ROLE,
        agegroupId,
        teamId: teamId,
        deposit: this.feeForm.playerDeposit,
        balanceDue: this.feeForm.playerBalanceDue,
        bFullPaymentRequired: this.feeForm.playerPhase,
        repriceExisting,
        modifiers: this.playerModifiers
      }));
    } else if (this.playerFeeId) {
      saves.push(this.ladtService.deleteFee(this.playerFeeId));
    }

    // Save club rep fee override (team-level) if any value set or modifiers exist
    const hasClubRepFee = this.feeForm.clubRepDeposit != null || this.feeForm.clubRepBalanceDue != null
        || this.feeForm.clubRepPhase != null || this.clubRepModifiers.length > 0;
    if (agegroupId && hasClubRepFee) {
      saves.push(this.ladtService.saveFee({
        roleId: CLUBREP_ROLE,
        agegroupId,
        teamId: teamId,
        deposit: this.feeForm.clubRepDeposit,
        balanceDue: this.feeForm.clubRepBalanceDue,
        bFullPaymentRequired: this.feeForm.clubRepPhase,
        repriceExisting,
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
        this.saveMessage.set(this.savedMessage(results, 'Team saved successfully.'));
        this.captureOriginals();
        // TODO: The 'emit' function requires a mandatory void argument
        this.saved.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to save team.');
      }
    });
  }

  onRepriceConfirm(): void {
    this.repriceDialog.set(null);
    this.performSave(true);
  }

  onRepriceDismiss(): void {
    const dlg = this.repriceDialog();
    this.repriceDialog.set(null);
    if (dlg?.isPhase) {
      this.feeForm.playerPhase = this.originalPhase.player;
      this.feeForm.clubRepPhase = this.originalPhase.clubRep;
      this.isSaving.set(false);
    } else {
      this.performSave(false);
    }
  }

  private savedMessage(results: any[], plain: string): string {
    const repriced = results.reduce(
      (sum, r) => sum + (r && typeof r === 'object' && 'registrationsRepriced' in r ? r.registrationsRepriced : 0), 0);
    return repriced > 0 ? `Saved. Repriced ${repriced} registration(s).` : plain;
  }

  private scopeLabel(): string {
    return this.team()?.teamName || 'this team';
  }

  private captureOriginals(): void {
    this.originalSnapshot = { player: this.feeSnapshot('player'), clubRep: this.feeSnapshot('clubRep') };
    this.originalPhase = { player: this.feeForm.playerPhase, clubRep: this.feeForm.clubRepPhase };
  }

  private roleChanged(role: 'player' | 'clubRep'): boolean {
    return this.feeSnapshot(role) !== this.originalSnapshot[role];
  }

  private feeSnapshot(role: 'player' | 'clubRep'): string {
    const dep = role === 'player' ? this.feeForm.playerDeposit : this.feeForm.clubRepDeposit;
    const bal = role === 'player' ? this.feeForm.playerBalanceDue : this.feeForm.clubRepBalanceDue;
    const phase = role === 'player' ? this.feeForm.playerPhase : this.feeForm.clubRepPhase;
    const mods = role === 'player' ? this.playerModifiers : this.clubRepModifiers;
    const modKey = mods
      .map(m => `${m.modifierType}:${m.amount}:${m.startDate}:${m.endDate}`)
      .sort()
      .join('|');
    return `${dep}|${bal}|${phase}|${modKey}`;
  }

  confirmDrop(): void {
    this.showDropConfirm.set(true);
  }

  doDrop(): void {
    this.isSaving.set(true);
    this.ladtService.dropTeam(this.teamId()).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.showDropConfirm.set(false);
        // TODO: The 'emit' function requires a mandatory void argument
        this.dropped.emit();
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
    this.showCloneDialog.set(true);
  }

  onCloneSuccess(newTeam: TeamDetailDto): void {
    this.showCloneDialog.set(false);
    this.isError.set(false);
    this.saveMessage.set('Team cloned successfully.');
    this.cloned.emit(newTeam.teamId);
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

    this.ladtService.moveTeamToClub(this.teamId(), request).subscribe({
      next: (result) => {
        this.isMoving.set(false);
        this.showChangeClub.set(false);
        this.isError.set(false);
        this.saveMessage.set(result.message);
        this.loadDetail();
        // TODO: The 'emit' function requires a mandatory void argument
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
