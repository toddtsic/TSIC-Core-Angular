import { ChangeDetectionStrategy, Component, OnChanges, OnInit, OnDestroy, HostListener, computed, signal, inject, input, output, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { LadtService } from '../services/ladt.service';
import { LadtEditGuardService } from '../services/ladt-edit-guard.service';
import { FeeRepriceService } from '../services/fee-reprice.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import { FeeCardComponent, type ModifierForm } from './fee-card.component';
import { RepriceConfirmComponent, type RepriceDialog } from './reprice-confirm.component';
import { CloneAgegroupDialogComponent } from './clone-agegroup-dialog.component';
import type { AgegroupDetailDto, UpdateAgegroupRequest, JobFeeDto, FeeModifierDto } from '../../../../core/api';
import { AGEGROUP_COLORS } from '../../../scheduling/shared/utils/scheduling-helpers';
import { JobService } from '../../../../infrastructure/services/job.service';

const PLAYER_ROLE = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
const CLUBREP_ROLE = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';
const JOB_TYPE_TOURNAMENT = 2;

@Component({
  selector: 'app-agegroup-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, FeeCardComponent, RepriceConfirmComponent, CloneAgegroupDialogComponent],
  template: `
    <div class="detail-header d-flex align-items-center justify-content-between">
      <div class="d-flex align-items-center gap-2">
        <i class="bi bi-people text-success"></i>
        <h5 class="mb-0">Age Group Details</h5>
      </div>
      <div class="d-flex gap-2">
        <button class="btn btn-sm btn-outline-secondary" (click)="openCloneDialog()" [disabled]="isSaving() || !agegroup()" title="Clone age group">
          <i class="bi bi-copy me-1"></i>Clone
        </button>
        <button class="btn btn-sm btn-outline-danger" (click)="confirmDelete()"
                [disabled]="isSaving() || !canDelete()"
                [title]="!canDelete() ? 'Remove all teams before deleting this age group' : 'Delete this age group'">
          <i class="bi bi-trash me-1"></i>Delete
        </button>
      </div>
    </div>

    @if (showCloneDialog() && agegroup(); as ag) {
      <app-clone-agegroup-dialog
        [sourceAgegroupId]="ag.agegroupId"
        [sourceAgegroupName]="ag.agegroupName || ''"
        (cancelled)="showCloneDialog.set(false)"
        (cloned)="onCloneSuccess($event)" />
    }

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
            namePrefix="clubRep" [deposit]="feeForm.clubRepDeposit" (depositChange)="feeForm.clubRepDeposit = $event; clearFeeError()"
            [balanceDue]="feeForm.clubRepBalanceDue" (balanceDueChange)="feeForm.clubRepBalanceDue = $event; clearFeeError()"
            [bFullPaymentRequired]="feeForm.clubRepPhase" (bFullPaymentRequiredChange)="onPhaseToggle('clubRep', $event)"
            [modifiers]="clubRepModifiers" [scope]="'agegroup'" [phaseNote]="phaseNote('clubRep')"
            hintText="Age group default for every team in it, unless a team sets its own. Overrides the league. Most-specific wins (never stacked)." />
          <app-fee-card header="Player Fees" headerIcon="bi-person" variant="player"
            namePrefix="player" [deposit]="feeForm.playerDeposit" (depositChange)="feeForm.playerDeposit = $event; clearFeeError()"
            [balanceDue]="feeForm.playerBalanceDue" (balanceDueChange)="feeForm.playerBalanceDue = $event; clearFeeError()"
            [bFullPaymentRequired]="feeForm.playerPhase" (bFullPaymentRequiredChange)="onPhaseToggle('player', $event)"
            [modifiers]="playerModifiers" placeholder="Optional" [scope]="'agegroup'" [phaseNote]="phaseNote('player')"
            hintText="Age group default for every team in it, unless a team sets its own. Overrides the league. Most-specific wins (never stacked)." />
        } @else {
          <app-fee-card header="Player Fees" headerIcon="bi-person" variant="player"
            namePrefix="player" [deposit]="feeForm.playerDeposit" (depositChange)="feeForm.playerDeposit = $event; clearFeeError()"
            [balanceDue]="feeForm.playerBalanceDue" (balanceDueChange)="feeForm.playerBalanceDue = $event; clearFeeError()"
            [bFullPaymentRequired]="feeForm.playerPhase" (bFullPaymentRequiredChange)="onPhaseToggle('player', $event)"
            [modifiers]="playerModifiers" placeholder="Optional" [scope]="'agegroup'" [phaseNote]="phaseNote('player')"
            hintText="Age group default for every team in it, unless a team sets its own. Overrides the league. Most-specific wins (never stacked)." />
          <app-fee-card header="Club Rep / Team Fees" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [deposit]="feeForm.clubRepDeposit" (depositChange)="feeForm.clubRepDeposit = $event; clearFeeError()"
            [balanceDue]="feeForm.clubRepBalanceDue" (balanceDueChange)="feeForm.clubRepBalanceDue = $event; clearFeeError()"
            [bFullPaymentRequired]="feeForm.clubRepPhase" (bFullPaymentRequiredChange)="onPhaseToggle('clubRep', $event)"
            [modifiers]="clubRepModifiers" [scope]="'agegroup'" [phaseNote]="phaseNote('clubRep')"
            hintText="Age group default for every team in it, unless a team sets its own. Overrides the league. Most-specific wins (never stacked)." />
        }

        <!-- ── Save (sticky footer) ── -->
        <div class="detail-save-bar" [class.is-dirty]="isDirty()" [class.is-confirming]="repriceDialog() !== null">
          @if (repriceDialog(); as dlg) {
            <app-reprice-confirm
              [dialog]="dlg"
              (updateAll)="onRepriceConfirm()"
              (convert)="onPhaseConvert($event)"
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
export class AgegroupDetailComponent implements OnChanges, OnInit, OnDestroy {
  readonly agegroupId = input.required<string>();
  /** Parent league (the level-1 node's parentId). Enables the "apply phase to every age
   *  group in this league" scope choice on a phase flip; null = single-age-group only. */
  readonly leagueId = input<string | null>(null);
  readonly canDelete = input(true);
  readonly playerCount = input(0);
  readonly saved = output<void>();
  readonly deleted = output<void>();
  readonly cloned = output<string>();

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

  /** Unsaved-changes probe — NgForm.dirty covers settings + fee-card controls (all
   *  render inside this form). See LadtEditGuardService. */
  readonly isDirty = (): boolean => this.detailForm()?.dirty ?? false;
  private readonly dirtyProbe = () => this.isDirty();

  readonly isTournament = computed(() => this.jobService.currentJob()?.jobTypeId === JOB_TYPE_TOURNAMENT);

  agegroup = signal<AgegroupDetailDto | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);
  isError = signal(false);
  showDeleteConfirm = signal(false);
  showCloneDialog = signal(false);
  colorDropdownOpen = signal(false);

  colorOptions = AGEGROUP_COLORS;
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
  // leagueScope (phase flips only) carries the this-vs-all-age-groups counts for the selector.
  repriceDialog = signal<RepriceDialog | null>(null);

  /** Set when a phase Convert chose "all age groups" — the roles to fan out across the league
   *  after this age group's own save completes. Consumed (and cleared) in performSave. */
  private leagueApplyRoles: ('player' | 'clubRep')[] | null = null;

  // Snapshots taken at load + after each successful save, to detect what changed.
  private originalSnapshot = { player: '', clubRep: '' };
  private originalPhase = { player: null as boolean | null, clubRep: null as boolean | null };
  private playerFeeId: string | null = null;
  private clubRepFeeId: string | null = null;

  @HostListener('document:click')
  onDocumentClick(): void {
    this.colorDropdownOpen.set(false);
  }

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
    this.showDeleteConfirm.set(false);

    forkJoin({
      detail: this.ladtService.getAgegroup(this.agegroupId()),
      fees: this.ladtService.getAgegroupFees(this.agegroupId())
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
      playerPhase: playerFee?.bFullPaymentRequired ?? null,
      clubRepDeposit: clubRepFee?.deposit ?? null,
      clubRepBalanceDue: clubRepFee?.balanceDue ?? null,
      clubRepPhase: clubRepFee?.bFullPaymentRequired ?? null
    };

    this.playerModifiers = (playerFee?.modifiers ?? []).map(m => this.toModifierForm(m));
    this.clubRepModifiers = (clubRepFee?.modifiers ?? []).map(m => this.toModifierForm(m));

    this.captureOriginals();
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
    const feeError = this.depositBalanceError();
    if (feeError) {
      this.isError.set(true);
      this.saveMessage.set(feeError);
      return;
    }

    const playerChanged = this.roleChanged('player');
    const clubRepChanged = this.roleChanged('clubRep');
    this.feeChangedPending = playerChanged || clubRepChanged;
    this.phaseFlipPending = (playerChanged && this.feeForm.playerPhase !== this.originalPhase.player)
                         || (clubRepChanged && this.feeForm.clubRepPhase !== this.originalPhase.clubRep);

    // Nothing fee-related changed → straight save (no reprice, no prompt).
    if (!playerChanged && !clubRepChanged) {
      this.performSave(false);
      return;
    }

    // Payment-phase flips are decided AT THE TOGGLE now (onPhaseToggle → openPhaseScopePrompt
    // fires the "all age groups / just this" choice the moment the phase is flipped, and records
    // the scope in leagueApplyRoles). The Save button isn't even reachable until that prompt is
    // answered (it replaces the button), so by the time we're here the scope is chosen — commit
    // it retroactively (a phase flip is always retroactive) and fan out only if "all" was picked.
    if (this.phaseFlipPending) {
      this.performSave(true);
      return;
    }

    // Amount/modifier change only → the save-time "update existing, or future only?" prompt.
    this.isSaving.set(true);
    this.saveMessage.set(null);
    this.feeReprice.getBlastArea(
      { agegroupId: this.agegroupId() },
      { player: playerChanged, clubRep: clubRepChanged }
    ).subscribe({
      next: (blast) => {
        // No existing registrations in scope → just save the config (nothing to reprice).
        if (blast.playerCount + blast.teamCount === 0) {
          this.performSave(false);
          return;
        }
        this.repriceDialog.set({
          isPhase: false,
          message: this.feeReprice.buildMessage(blast, this.scopeLabel(), false)
        });
        this.isSaving.set(false);
      },
      // Count probe failed — don't block the save; persist config without repricing.
      error: () => this.performSave(false)
    });
  }

  /**
   * Payment-phase toggle handler. Flipping the phase is a decision in its own right, so the
   * "convert all age groups in this league, or just this one?" question is asked RIGHT HERE —
   * the instant you flip it — not deferred into the Save action. The chosen scope is recorded
   * (onPhaseConvert) and committed when you click Save.
   */
  onPhaseToggle(role: 'player' | 'clubRep', value: boolean | null): void {
    if (role === 'player') this.feeForm.playerPhase = value;
    else this.feeForm.clubRepPhase = value;
    this.markFeeDirty();
    this.openPhaseScopePrompt();
  }

  /** Open (or clear) the phase-conversion scope prompt for the currently-flipped role(s). */
  private openPhaseScopePrompt(): void {
    const playerFlipped = this.feeForm.playerPhase !== this.originalPhase.player;
    const clubRepFlipped = this.feeForm.clubRepPhase !== this.originalPhase.clubRep;

    // Toggled back to the saved value → nothing to convert; drop any open phase prompt.
    if (!playerFlipped && !clubRepFlipped) {
      this.leagueApplyRoles = null;
      if (this.repriceDialog()?.isPhase) this.repriceDialog.set(null);
      return;
    }

    // A fresh flip supersedes any prior "all/this" pick until the new prompt is answered.
    this.leagueApplyRoles = null;
    const roles = { player: playerFlipped, clubRep: clubRepFlipped };
    this.feeReprice.getBlastArea({ agegroupId: this.agegroupId() }, roles).subscribe({
      next: (blast) => {
        const thisCount = blast.playerCount + blast.teamCount;
        // No existing registrations in scope → no conversion decision; Save just persists the flip.
        if (thisCount === 0) {
          if (this.repriceDialog()?.isPhase) this.repriceDialog.set(null);
          return;
        }
        const leagueId = this.leagueId();
        if (leagueId) {
          this.feeReprice.getBlastArea({ leagueId }, roles).subscribe({
            next: (lb) => this.repriceDialog.set({
              isPhase: true,
              message: 'Convert existing registrations to the new payment phase. Choose how widely to apply it:',
              leagueScope: { thisCount, allCount: lb.playerCount + lb.teamCount }
            }),
            // League count probe failed → single-age-group confirm (no fan-out option).
            error: () => this.repriceDialog.set({
              isPhase: true,
              message: this.feeReprice.buildMessage(blast, this.scopeLabel(), true)
            })
          });
        } else {
          this.repriceDialog.set({
            isPhase: true,
            message: this.feeReprice.buildMessage(blast, this.scopeLabel(), true)
          });
        }
      },
      // Count probe failed → don't block; Save will still persist the flip (no fan-out).
      error: () => { if (this.repriceDialog()?.isPhase) this.repriceDialog.set(null); }
    });
  }

  onRepriceConfirm(): void {
    this.repriceDialog.set(null);
    this.performSave(true);   // "Update all" (amount change) → retroactive reprice
  }

  /**
   * Phase "Convert" — the scope prompt IS the confirmation (counts already shown), so this commits
   * immediately rather than staging the choice for a later Save. scope='all' fans the flip across
   * every age group in the league (captured in leagueApplyRoles, consumed by performSave);
   * scope='this' converts this age group only. Saving the phase saves the whole age-group form,
   * so any other pending edits commit alongside it (a phase flip is always retroactive). Cancel
   * (onRepriceDismiss) reverts the toggle and writes nothing.
   */
  onPhaseConvert(scope: 'this' | 'all'): void {
    this.leagueApplyRoles = scope === 'all' ? this.flippedPhaseRoles() : null;
    this.repriceDialog.set(null);
    this.performSave(true);
  }

  /** Roles whose payment phase differs from the last-saved snapshot (the ones being flipped). */
  private flippedPhaseRoles(): ('player' | 'clubRep')[] {
    const roles: ('player' | 'clubRep')[] = [];
    if (this.feeForm.playerPhase !== this.originalPhase.player) roles.push('player');
    if (this.feeForm.clubRepPhase !== this.originalPhase.clubRep) roles.push('clubRep');
    return roles;
  }

  /**
   * After this age group saved + repriced (thisCount), stamp the flipped phase onto the league's
   * other age groups via the canonical endpoint, then toast the combined league-wide total. The
   * endpoint's whole-job reprice is idempotent, so this age group's already-converted rows
   * recompute unchanged and aren't double-counted — its registrations are in `thisCount`, the
   * siblings' in the endpoint's `registrationsRepriced`.
   */
  private applyLeagueThenFinish(thisCount: number, roles: ('player' | 'clubRep')[]): void {
    const leagueId = this.leagueId();
    if (!leagueId || roles.length === 0) {
      this.finishLeagueApply(thisCount, 0, 1);
      return;
    }
    const calls = roles.map(role => this.ladtService.applyLeaguePhase(leagueId, {
      roleId: role === 'player' ? PLAYER_ROLE : CLUBREP_ROLE,
      bFullPaymentRequired: role === 'player' ? this.feeForm.playerPhase : this.feeForm.clubRepPhase
    }));
    forkJoin(calls).subscribe({
      next: (responses) => {
        const others = responses.reduce((sum, r) => sum + r.registrationsRepriced, 0);
        const ags = responses.reduce((max, r) => Math.max(max, r.agegroupsApplied), 0);
        this.finishLeagueApply(thisCount, others, ags);
      },
      // This age group already saved + converted; the fan-out failed. Report what did land.
      error: () => {
        this.isSaving.set(false);
        this.toast.show(this.feeReprice.phaseToastMessage(thisCount, 'age-group'), 'warning');
        this.saved.emit();
      }
    });
  }

  private finishLeagueApply(thisCount: number, otherCount: number, agegroupsApplied: number): void {
    this.isSaving.set(false);
    const total = thisCount + otherCount;
    const ags = Math.max(agegroupsApplied, 1);
    this.toast.show(
      `Converted ${total} registration${total === 1 ? '' : 's'} across ${ags} age group${ags === 1 ? '' : 's'}.`,
      'success', 10000);
    this.saved.emit();
  }

  onRepriceDismiss(): void {
    const dlg = this.repriceDialog();
    this.repriceDialog.set(null);
    if (dlg?.isPhase) {
      // Cancelled the phase conversion → revert the toggle (don't persist it) and the scope pick.
      this.feeForm.playerPhase = this.originalPhase.player;
      this.feeForm.clubRepPhase = this.originalPhase.clubRep;
      this.leagueApplyRoles = null;
      this.isSaving.set(false);
    } else {
      // Amount change "Future only" → save the config, leave existing registrations untouched.
      this.performSave(false);
    }
  }

  /** "Keep editing" — collapse the inline confirm and save nothing; stay in the editor. */
  onRepriceCancel(): void {
    this.repriceDialog.set(null);
    this.isSaving.set(false);
  }

  private performSave(repriceExisting: boolean): void {
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
      this.ladtService.updateAgegroup(this.agegroupId(), request)
    ];

    const agegroupId = this.agegroupId();
    if (this.feeForm.playerDeposit != null || this.feeForm.playerBalanceDue != null
        || this.feeForm.playerPhase != null || this.playerModifiers.length > 0) {
      saves.push(this.ladtService.saveFee({
        roleId: PLAYER_ROLE,
        agegroupId: agegroupId,
        deposit: this.feeForm.playerDeposit,
        balanceDue: this.feeForm.playerBalanceDue,
        bFullPaymentRequired: this.feeForm.playerPhase,
        repriceExisting,
        modifiers: this.toModifierDtos(this.playerModifiers)
      }));
    } else if (this.playerFeeId) {
      saves.push(this.ladtService.deleteFee(this.playerFeeId));
    }

    if (this.feeForm.clubRepDeposit != null || this.feeForm.clubRepBalanceDue != null
        || this.feeForm.clubRepPhase != null || this.clubRepModifiers.length > 0) {
      saves.push(this.ladtService.saveFee({
        roleId: CLUBREP_ROLE,
        agegroupId: agegroupId,
        deposit: this.feeForm.clubRepDeposit,
        balanceDue: this.feeForm.clubRepBalanceDue,
        bFullPaymentRequired: this.feeForm.clubRepPhase,
        repriceExisting,
        modifiers: this.toModifierDtos(this.clubRepModifiers)
      }));
    } else if (this.clubRepFeeId) {
      saves.push(this.ladtService.deleteFee(this.clubRepFeeId));
    }

    forkJoin(saves).subscribe({
      next: (results) => {
        this.isError.set(false);
        this.saveMessage.set(this.savedMessage(results, 'Age group saved successfully.'));
        this.captureOriginals();

        // "Apply to all age groups" chosen → keep the spinner up while the phase fans out
        // across the league, then toast the combined total (handles its own emit).
        const applyRoles = this.leagueApplyRoles;
        this.leagueApplyRoles = null;
        if (applyRoles && applyRoles.length > 0) {
          this.applyLeagueThenFinish(this.feeReprice.repricedCount(results), applyRoles);
          return;
        }

        this.isSaving.set(false);
        const toastMsg = this.feeReprice.saveToastMessage(results, this.phaseFlipPending, this.feeChangedPending, 'age-group');
        if (toastMsg) this.toast.show(toastMsg, 'success', 10000);
        this.saved.emit();
      },
      error: (err) => {
        this.leagueApplyRoles = null;   // the save failed; don't fan out on a later save
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to save age group.');
      }
    });
  }

  /** "Saved. Repriced N registration(s)." when any fee save repriced existing rows. */
  private savedMessage(results: any[], plain: string): string {
    const who = this.feeReprice.describeReprice(results);
    return who ? `Saved. Repriced ${who}.` : plain;
  }

  private scopeLabel(): string {
    return this.agegroup()?.agegroupName || 'this age group';
  }

  /**
   * Blocks an invalid deposit-without-balance fee (a deposit needs a balance to defer to).
   * Mirrors the backend FeeController guard so the director gets immediate feedback.
   */
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
   * Read-only phase pointer for the age-group card. When an age-group fee exists, the card's
   * own toggle + amount-aware explanation own the phase display, so this returns null. When no
   * fee is set here, phase can still be overridden one tier down — point there (mirrors the
   * "See team settings" fallback in the age-group grid's Payment Phase column).
   */
  phaseNote(role: 'player' | 'clubRep'): string | null {
    const dep = role === 'player' ? this.feeForm.playerDeposit : this.feeForm.clubRepDeposit;
    const bal = role === 'player' ? this.feeForm.playerBalanceDue : this.feeForm.clubRepBalanceDue;
    if (dep != null || bal != null) return null;
    return 'See team settings.';
  }

  private captureOriginals(): void {
    this.originalSnapshot = { player: this.feeSnapshot('player'), clubRep: this.feeSnapshot('clubRep') };
    this.originalPhase = { player: this.feeForm.playerPhase, clubRep: this.feeForm.clubRepPhase };
  }

  private roleChanged(role: 'player' | 'clubRep'): boolean {
    return this.feeSnapshot(role) !== this.originalSnapshot[role];
  }

  /** Comparable string of a role's money state (deposit, balance, phase, modifiers). */
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

  getColorName(hex: string): string {
    return this.colorOptions.find(c => c.value === hex)?.name ?? hex;
  }

  selectColor(value: string | null): void {
    if (value !== this.form.color) {
      this.form.color = value;
      // The color picker is a dropdown, not a named <input>, so NgForm can't see this
      // change. Flag the form dirty so the sticky save bar lights and the discard guard
      // covers a color-only edit.
      this.markFeeDirty();
    }
    this.colorDropdownOpen.set(false);
  }

  /** Flag the form dirty for a control NgForm can't see on its own — the phase toggle (a
   *  bare checkbox, no ngModel) and the color picker. Deposit/balance/modifier ngModels now
   *  register with this form via fee-card's ControlContainer, so they dirty it directly. */
  markFeeDirty(): void {
    this.detailForm()?.form.markAsDirty();
  }

  openCloneDialog(): void {
    this.showCloneDialog.set(true);
  }

  onCloneSuccess(clone: AgegroupDetailDto): void {
    this.showCloneDialog.set(false);
    this.saveMessage.set('Age group cloned successfully.');
    this.isError.set(false);
    this.cloned.emit(clone.agegroupId);
  }

  confirmDelete(): void {
    this.showDeleteConfirm.set(true);
  }

  doDelete(): void {
    this.isSaving.set(true);
    this.ladtService.deleteAgegroup(this.agegroupId()).subscribe({
      next: () => {
        this.isSaving.set(false);
        // TODO: The 'emit' function requires a mandatory void argument
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

}
