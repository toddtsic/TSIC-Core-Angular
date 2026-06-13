import { ChangeDetectionStrategy, Component, OnChanges, OnInit, OnDestroy, computed, signal, inject, input, output, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { LadtService } from '../services/ladt.service';
import { LadtEditGuardService } from '../services/ladt-edit-guard.service';
import { FeeRepriceService } from '../services/fee-reprice.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import { FeeCardComponent, type ModifierForm } from './fee-card.component';
import { ConfirmDialogComponent } from '../../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import { RepriceConfirmComponent } from './reprice-confirm.component';
import { CloneTeamDialogComponent } from './clone-team-dialog.component';
import { JobService } from '../../../../infrastructure/services/job.service';
import type { TeamDetailDto, UpdateTeamRequest, ClubRegistrationDto, MoveTeamToClubRequest, JobFeeDto } from '../../../../core/api';

const PLAYER_ROLE = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
const CLUBREP_ROLE = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';
const JOB_TYPE_TOURNAMENT = 2;

@Component({
  selector: 'app-team-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, FeeCardComponent, ConfirmDialogComponent, RepriceConfirmComponent, CloneTeamDialogComponent],
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
        <div class="section-card settings-card" [class.section-locked]="settingsLocked()">
          <div class="section-card-header">
            <i class="bi bi-gear"></i> Settings
          </div>
          <div class="d-flex align-items-end gap-2 mb-2">
            <div class="flex-grow-1">
              <label class="fee-label">Team Name</label>
              <input class="form-control form-control-sm" [(ngModel)]="form.teamName" name="teamName" (ngModelChange)="onSettingsChange()">
            </div>
            <div class="form-check form-switch" style="padding-bottom: 4px;">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.active" name="active" (ngModelChange)="onSettingsChange()">
              <label class="form-check-label">Active</label>
            </div>
          </div>
          <div class="d-flex align-items-center gap-2 mb-2">
            <label class="fee-label">Max Roster</label>
            <input class="form-control form-control-sm" type="number" [(ngModel)]="form.maxCount" name="maxCount" style="width: 80px;" (ngModelChange)="onSettingsChange()">
          </div>
          <div class="settings-grid">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bAllowSelfRostering" name="bAllowSelfRostering" (ngModelChange)="onSettingsChange()">
              <label class="form-check-label">Self Rostering</label>
            </div>
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideRoster" name="bHideRoster" (ngModelChange)="onSettingsChange()">
              <label class="form-check-label">Hide Roster</label>
            </div>
          </div>
        </div>

        <!-- ── Dates ── -->
        <div class="section-card" [class.section-locked]="settingsLocked()">
          <div class="section-card-header">
            <i class="bi bi-calendar3"></i> Dates
          </div>
          <div class="row g-2">
            <div class="col-6">
              <label class="fee-label">Start Date</label>
              <input class="form-control form-control-sm" type="date" [(ngModel)]="form.startdate" name="startdate" (ngModelChange)="onSettingsChange()">
            </div>
            <div class="col-6">
              <label class="fee-label">End Date</label>
              <input class="form-control form-control-sm" type="date" [(ngModel)]="form.enddate" name="enddate" (ngModelChange)="onSettingsChange()">
            </div>
            <div class="col-6">
              <label class="fee-label">Effective As Of</label>
              <input class="form-control form-control-sm" type="date" [(ngModel)]="form.effectiveasofdate" name="effectiveasofdate" (ngModelChange)="onSettingsChange()">
            </div>
            <div class="col-6">
              <label class="fee-label">Expire On</label>
              <input class="form-control form-control-sm" type="date" [(ngModel)]="form.expireondate" name="expireondate" (ngModelChange)="onSettingsChange()">
            </div>
          </div>
        </div>

        @if (isTournament()) {
          <app-fee-card header="Club Rep Fee Override" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [deposit]="feeForm.clubRepDeposit" (depositChange)="feeForm.clubRepDeposit = $event; onFeeAmountStart(); clearFeeError()"
            [balanceDue]="feeForm.clubRepBalanceDue" (balanceDueChange)="feeForm.clubRepBalanceDue = $event; onFeeAmountStart(); clearFeeError()"
            [bFullPaymentRequired]="feeForm.clubRepPhase" (bFullPaymentRequiredChange)="onPhaseToggle('clubRep', $event)"
            [modifiers]="clubRepModifiers" [phaseNote]="phaseNote('clubRep')"
            [amountsDisabled]="feesAmountLocked()" [toggleDisabled]="feesPhaseLocked()" (amountCommitted)="onFeeAmountCommitted()"
            hintText="Team override — applies only to this team. Overrides the age group and league. Most-specific wins (never stacked). Leave blank to inherit."
            placeholder="Agegroup default" [scope]="'team'" />
          <app-fee-card header="Player Fee Override" headerIcon="bi-person" variant="player"
            namePrefix="player" [deposit]="feeForm.playerDeposit" (depositChange)="feeForm.playerDeposit = $event; onFeeAmountStart(); clearFeeError()"
            [balanceDue]="feeForm.playerBalanceDue" (balanceDueChange)="feeForm.playerBalanceDue = $event; onFeeAmountStart(); clearFeeError()"
            [bFullPaymentRequired]="feeForm.playerPhase" (bFullPaymentRequiredChange)="onPhaseToggle('player', $event)"
            [modifiers]="playerModifiers" [phaseNote]="phaseNote('player')"
            [amountsDisabled]="feesAmountLocked()" [toggleDisabled]="feesPhaseLocked()" (amountCommitted)="onFeeAmountCommitted()"
            hintText="Team override — applies only to this team. Overrides the age group and league. Most-specific wins (never stacked). Leave blank to inherit."
            placeholder="Agegroup default" [scope]="'team'" />
        } @else {
          <app-fee-card header="Player Fee Override" headerIcon="bi-person" variant="player"
            namePrefix="player" [deposit]="feeForm.playerDeposit" (depositChange)="feeForm.playerDeposit = $event; onFeeAmountStart(); clearFeeError()"
            [balanceDue]="feeForm.playerBalanceDue" (balanceDueChange)="feeForm.playerBalanceDue = $event; onFeeAmountStart(); clearFeeError()"
            [bFullPaymentRequired]="feeForm.playerPhase" (bFullPaymentRequiredChange)="onPhaseToggle('player', $event)"
            [modifiers]="playerModifiers" [phaseNote]="phaseNote('player')"
            [amountsDisabled]="feesAmountLocked()" [toggleDisabled]="feesPhaseLocked()" (amountCommitted)="onFeeAmountCommitted()"
            hintText="Team override — applies only to this team. Overrides the age group and league. Most-specific wins (never stacked). Leave blank to inherit."
            placeholder="Agegroup default" [scope]="'team'" />
          <app-fee-card header="Club Rep Fee Override" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [deposit]="feeForm.clubRepDeposit" (depositChange)="feeForm.clubRepDeposit = $event; onFeeAmountStart(); clearFeeError()"
            [balanceDue]="feeForm.clubRepBalanceDue" (balanceDueChange)="feeForm.clubRepBalanceDue = $event; onFeeAmountStart(); clearFeeError()"
            [bFullPaymentRequired]="feeForm.clubRepPhase" (bFullPaymentRequiredChange)="onPhaseToggle('clubRep', $event)"
            [modifiers]="clubRepModifiers" [phaseNote]="phaseNote('clubRep')"
            [amountsDisabled]="feesAmountLocked()" [toggleDisabled]="feesPhaseLocked()" (amountCommitted)="onFeeAmountCommitted()"
            hintText="Team override — applies only to this team. Overrides the age group and league. Most-specific wins (never stacked). Leave blank to inherit."
            placeholder="Agegroup default" [scope]="'team'" />
        }

        <!-- ── Eligibility ── -->
        <div class="section-card" [class.section-locked]="settingsLocked()">
          <div class="section-card-header">
            <i class="bi bi-funnel"></i> Eligibility
          </div>
          <div class="d-flex gap-2">
            <div style="min-width: 100px;">
              <label class="fee-label">Gender</label>
              <select class="form-select form-select-sm" [(ngModel)]="form.gender" name="gender" (ngModelChange)="onSettingsChange()">
                <option [ngValue]="null">Any</option>
                <option value="M">Male</option>
                <option value="F">Female</option>
                <option value="C">Co-Ed</option>
              </select>
            </div>
            <div style="min-width: 120px;">
              <label class="fee-label">Level of Play</label>
              <select class="form-select form-select-sm" [(ngModel)]="form.levelOfPlay" name="levelOfPlay" (ngModelChange)="onSettingsChange()">
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
        <div class="section-card" [class.section-locked]="settingsLocked()">
          <div class="section-card-header">
            <i class="bi bi-chat-text"></i> Notes
          </div>
          <div class="row g-2">
            <div class="col-6">
              <label class="fee-label">Requests</label>
              <textarea class="form-control form-control-sm" rows="2" [(ngModel)]="form.requests" name="requests" (ngModelChange)="onSettingsChange()"></textarea>
            </div>
            <div class="col-6">
              <label class="fee-label">Team Comments</label>
              <textarea class="form-control form-control-sm" rows="2" [(ngModel)]="form.teamComments" name="teamComments" (ngModelChange)="onSettingsChange()"></textarea>
            </div>
          </div>
        </div>

        <!-- ── Save (sticky footer) ── -->
        <div class="detail-save-bar" [class.is-dirty]="isDirty()" [class.is-confirming]="repriceDialog() !== null">
          @if (repriceDialog(); as dlg) {
            <app-reprice-confirm
              [dialog]="dlg"
              (updateAll)="onRepriceConfirm()"
              (convert)="onRepriceConfirm()"
              (secondary)="onRepriceDismiss()"
              (keepEditing)="onRepriceCancel()" />
          } @else {
            <button type="submit" class="btn btn-sm btn-primary px-4 detail-save-btn"
                    [class.pulse]="isDirty()" [disabled]="isSaving()">
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
            } @else if (isDirty()) {
              <span class="small unsaved-hint text-warning-emphasis">
                <i class="bi bi-exclamation-circle me-1"></i>Unsaved changes
              </span>
            }
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
export class TeamDetailComponent implements OnChanges, OnInit, OnDestroy {
  readonly teamId = input.required<string>();
  readonly saved = output<void>();
  readonly cloned = output<string>();
  readonly clubChanged = output<void>();
  readonly dropped = output<void>();

  private readonly ladtService = inject(LadtService);
  private readonly jobService = inject(JobService);
  private readonly feeReprice = inject(FeeRepriceService);
  private readonly editGuard = inject(LadtEditGuardService);
  private readonly toast = inject(ToastService);

  /** Set per save() — true when this save flips a payment-phase toggle (either role/direction),
   *  so performSave can fire the quantified success toast on completion. */
  private phaseFlipPending = false;

  /** Set per save() — true when a player/club-rep fee value actually changed this save, so a
   *  fee change with nothing to reprice still confirms with a toast (vs an entity-only edit). */
  private feeChangedPending = false;

  private readonly detailForm = viewChild(NgForm);

  /** Unsaved-changes probe: NgForm.dirty covers every named control, including the
   *  fee-card inputs (they render inside this form). Stays conservative — a hand-reverted
   *  value still reads dirty, which is the safe side for a discard guard. */
  readonly isDirty = (): boolean => this.detailForm()?.dirty ?? false;
  private readonly dirtyProbe = () => this.isDirty();

  /** Flag the form dirty for the phase toggle (a bare checkbox with no ngModel, which NgForm
   *  can't see). Deposit/balance/modifier ngModels dirty the form directly via fee-card's
   *  ControlContainer registration. */
  markFeeDirty(): void {
    this.detailForm()?.form.markAsDirty();
  }

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

  editMode = signal<'fee-amount' | 'fee-phase' | 'settings' | null>(null);
  readonly feesAmountLocked = computed(() => this.editMode() === 'fee-phase' || this.editMode() === 'settings');
  readonly feesPhaseLocked = computed(() => this.editMode() === 'fee-amount' || this.editMode() === 'settings');
  readonly settingsLocked = computed(() => this.editMode() === 'fee-amount' || this.editMode() === 'fee-phase');

  onFeeAmountStart(): void {
    if (this.editMode() === null) this.editMode.set('fee-amount');
  }

  onFeeAmountCommitted(): void {
    const playerChanged = this.roleChanged('player');
    const clubRepChanged = this.roleChanged('clubRep');
    if (!playerChanged && !clubRepChanged) {
      if (this.editMode() === 'fee-amount') this.editMode.set(null);
      return;
    }
    this.editMode.set('fee-amount');
    this.markFeeDirty();
    this.feeReprice.getBlastArea(
      { teamId: this.teamId() },
      { player: playerChanged, clubRep: clubRepChanged }
    ).subscribe({
      next: (blast) => {
        if (blast.playerCount + blast.teamCount === 0) return;
        this.repriceDialog.set({
          isPhase: false,
          message: this.feeReprice.buildMessage(blast, this.scopeLabel(), false)
        });
      },
      error: () => {}
    });
  }

  onPhaseToggle(role: 'player' | 'clubRep', value: boolean | null): void {
    if (role === 'player') this.feeForm.playerPhase = value;
    else this.feeForm.clubRepPhase = value;
    this.editMode.set('fee-phase');
    this.markFeeDirty();
    this.openPhasePrompt();
  }

  private openPhasePrompt(): void {
    const playerFlipped = this.feeForm.playerPhase !== this.originalPhase.player;
    const clubRepFlipped = this.feeForm.clubRepPhase !== this.originalPhase.clubRep;
    if (!playerFlipped && !clubRepFlipped) {
      this.editMode.set(null);
      if (this.repriceDialog()?.isPhase) this.repriceDialog.set(null);
      return;
    }
    const roles = { player: playerFlipped, clubRep: clubRepFlipped };
    this.feeReprice.getBlastArea({ teamId: this.teamId() }, roles).subscribe({
      next: (blast) => {
        if (blast.playerCount + blast.teamCount === 0) {
          if (this.repriceDialog()?.isPhase) this.repriceDialog.set(null);
          return;
        }
        this.repriceDialog.set({
          isPhase: true,
          message: this.feeReprice.buildMessage(blast, this.scopeLabel(), true)
        });
      },
      error: () => { if (this.repriceDialog()?.isPhase) this.repriceDialog.set(null); }
    });
  }

  onSettingsChange(): void {
    if (this.editMode() === null) this.editMode.set('settings');
  }

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

  ngOnInit(): void {
    this.editGuard.register(this.dirtyProbe);
  }

  ngOnDestroy(): void {
    this.editGuard.unregister(this.dirtyProbe);
  }

  ngOnChanges(): void {
    this.loadDetail();
  }

  private loadDetail(): void {
    this.isLoading.set(true);
    this.saveMessage.set(null);
    this.showDropConfirm.set(false);
    this.showChangeClub.set(false);
    this.editMode.set(null);

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
    const depErr = this.depositBalanceError();
    if (depErr) {
      this.isError.set(true);
      this.saveMessage.set(depErr);
      return;
    }

    const playerChanged = this.roleChanged('player');
    const clubRepChanged = this.roleChanged('clubRep');
    this.feeChangedPending = playerChanged || clubRepChanged;
    this.phaseFlipPending = (playerChanged && this.feeForm.playerPhase !== this.originalPhase.player)
                         || (clubRepChanged && this.feeForm.clubRepPhase !== this.originalPhase.clubRep);

    if (!playerChanged && !clubRepChanged) {
      this.performSave(false);
      return;
    }

    const phaseFlip = this.phaseFlipPending;

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
        this.editMode.set(null);
        const toastMsg = this.feeReprice.saveToastMessage(results, this.phaseFlipPending, this.feeChangedPending, 'team');
        if (toastMsg) this.toast.show(toastMsg, 'success', 10000);
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
    this.feeChangedPending = this.roleChanged('player') || this.roleChanged('clubRep');
    this.phaseFlipPending = false;
    this.repriceDialog.set(null);
    this.performSave(true);
  }

  onRepriceDismiss(): void {
    const dlg = this.repriceDialog();
    this.repriceDialog.set(null);
    if (dlg?.isPhase) {
      this.feeForm.playerPhase = this.originalPhase.player;
      this.feeForm.clubRepPhase = this.originalPhase.clubRep;
      this.editMode.set(null);
      this.isSaving.set(false);
    } else {
      this.feeChangedPending = this.roleChanged('player') || this.roleChanged('clubRep');
      this.phaseFlipPending = false;
      this.performSave(false);
    }
  }

  /** "Keep editing" — collapse the inline confirm and save nothing; stay in the editor. */
  onRepriceCancel(): void {
    this.repriceDialog.set(null);
    this.isSaving.set(false);
  }

  private savedMessage(results: any[], plain: string): string {
    const who = this.feeReprice.describeReprice(results);
    return who ? `Saved. Repriced ${who}.` : plain;
  }

  private scopeLabel(): string {
    return this.team()?.teamName || 'this team';
  }

  private depositBalanceError(): string | null {
    const bad = (dep: number | null, bal: number | null, who: string) =>
      (dep ?? 0) > 0 && !((bal ?? 0) > 0)
        ? `${who} fee: a deposit must also have a balance due.` : null;
    return bad(this.feeForm.playerDeposit, this.feeForm.playerBalanceDue, 'Player')
        ?? bad(this.feeForm.clubRepDeposit, this.feeForm.clubRepBalanceDue, 'Club Rep');
  }

  /** Clears a showing deposit/balance validation error once the inputs no longer violate it. */
  clearFeeError(): void {
    if (this.isError() && !this.depositBalanceError()) {
      this.isError.set(false);
      this.saveMessage.set(null);
    }
  }

  /**
   * Read-only phase pointer for the team card. When a team-level fee exists, the card's own
   * toggle + amount-aware explanation own the phase display, so this returns null. The team is
   * the leaf of the cascade — with no fee set here, phase is inherited from above, so point UP
   * (the league/age-group fly-ins point down; the leaf has nothing below it).
   */
  phaseNote(role: 'player' | 'clubRep'): string | null {
    const dep = role === 'player' ? this.feeForm.playerDeposit : this.feeForm.clubRepDeposit;
    const bal = role === 'player' ? this.feeForm.playerBalanceDue : this.feeForm.clubRepBalanceDue;
    if (dep != null || bal != null) return null;
    return 'Inherits from the age group.';
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
