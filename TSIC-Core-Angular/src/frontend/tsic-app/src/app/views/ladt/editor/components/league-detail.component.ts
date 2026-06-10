import { ChangeDetectionStrategy, Component, OnChanges, OnInit, OnDestroy, computed, signal, inject, input, output, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { LadtService } from '../services/ladt.service';
import { LadtEditGuardService } from '../services/ladt-edit-guard.service';
import { FeeRepriceService } from '../services/fee-reprice.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import { FeeCardComponent, type ModifierForm } from './fee-card.component';
import { RepriceConfirmComponent } from './reprice-confirm.component';
import { JobService } from '../../../../infrastructure/services/job.service';
import type { LeagueDetailDto, UpdateLeagueRequest, SportOptionDto, JobFeeDto, FeeModifierDto } from '../../../../core/api';

const PLAYER_ROLE = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
const CLUBREP_ROLE = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';
const JOB_TYPE_TOURNAMENT = 2;

@Component({
  selector: 'app-league-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, FeeCardComponent, RepriceConfirmComponent],
  template: `
    <div class="detail-header">
      <div class="d-flex align-items-center gap-2">
        <i class="bi bi-trophy text-primary"></i>
        <h5 class="mb-0">League Details</h5>
      </div>
    </div>

    @if (isLoading()) {
      <div class="text-center py-4">
        <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
      </div>
    } @else if (league()) {
      <form (ngSubmit)="save()">
        <!-- ── Settings ── -->
        <div class="section-card settings-card">
          <div class="section-card-header">
            <i class="bi bi-gear"></i> Settings
          </div>
          <div class="d-flex align-items-end gap-2 mb-2">
            <div class="flex-grow-1">
              <label class="fee-label">League Name</label>
              <input class="form-control form-control-sm" [(ngModel)]="form.leagueName" name="leagueName" required>
            </div>
            <div style="min-width: 120px;">
              <label class="fee-label">Sport</label>
              <select class="form-select form-select-sm" [(ngModel)]="form.sportId" name="sportId">
                @for (sport of sports(); track sport.sportId) {
                  <option [ngValue]="sport.sportId">{{ sport.sportName }}</option>
                }
              </select>
            </div>
          </div>
          <div class="settings-grid">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideContacts" name="bHideContacts">
              <label class="form-check-label">Hide Contacts</label>
            </div>
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideStandings" name="bHideStandings">
              <label class="form-check-label">Hide Standings</label>
            </div>
          </div>
        </div>

        <!-- ── Advanced ── -->
        <div class="section-card">
          <div class="section-card-header">
            <i class="bi bi-sliders"></i> Advanced
          </div>
          <div>
            <label class="fee-label">Reschedule Emails To (addon)</label>
            <input class="form-control form-control-sm" [(ngModel)]="form.rescheduleEmailsToAddon" name="rescheduleEmailsToAddon">
            <small class="form-text text-body-secondary" style="font-size: 0.7rem;">Separate emails with semi-colon, no spaces</small>
          </div>
        </div>

        <!-- ── League Fees (Deposit / Balance Due + Early Bird / Late Fee) ── -->
        @if (isTournament()) {
          <app-fee-card header="Club Rep / Team — League Fees" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [deposit]="feeForm.clubRepDeposit" (depositChange)="feeForm.clubRepDeposit = $event; clearFeeError()"
            [balanceDue]="feeForm.clubRepBalanceDue" (balanceDueChange)="feeForm.clubRepBalanceDue = $event; clearFeeError()"
            [bFullPaymentRequired]="feeForm.clubRepPhase" (bFullPaymentRequiredChange)="feeForm.clubRepPhase = $event; markFeeDirty()"
            [modifiers]="clubRepModifiers" [phaseNote]="phaseNote('clubRep')" [scope]="'league'"
            hintText="League default for every age group unless an age group or team sets its own. Most-specific wins (never stacked)." />
          <app-fee-card header="Player — League Fees" headerIcon="bi-person" variant="player"
            namePrefix="player" [deposit]="feeForm.playerDeposit" (depositChange)="feeForm.playerDeposit = $event; clearFeeError()"
            [balanceDue]="feeForm.playerBalanceDue" (balanceDueChange)="feeForm.playerBalanceDue = $event; clearFeeError()"
            [bFullPaymentRequired]="feeForm.playerPhase" (bFullPaymentRequiredChange)="feeForm.playerPhase = $event; markFeeDirty()"
            [modifiers]="playerModifiers" placeholder="Optional" [phaseNote]="phaseNote('player')" [scope]="'league'"
            hintText="League default for every age group unless an age group or team sets its own. Most-specific wins (never stacked)." />
        } @else {
          <app-fee-card header="Player — League Fees" headerIcon="bi-person" variant="player"
            namePrefix="player" [deposit]="feeForm.playerDeposit" (depositChange)="feeForm.playerDeposit = $event; clearFeeError()"
            [balanceDue]="feeForm.playerBalanceDue" (balanceDueChange)="feeForm.playerBalanceDue = $event; clearFeeError()"
            [bFullPaymentRequired]="feeForm.playerPhase" (bFullPaymentRequiredChange)="feeForm.playerPhase = $event; markFeeDirty()"
            [modifiers]="playerModifiers" placeholder="Optional" [phaseNote]="phaseNote('player')" [scope]="'league'"
            hintText="League default for every age group unless an age group or team sets its own. Most-specific wins (never stacked)." />
          <app-fee-card header="Club Rep / Team — League Fees" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [deposit]="feeForm.clubRepDeposit" (depositChange)="feeForm.clubRepDeposit = $event; clearFeeError()"
            [balanceDue]="feeForm.clubRepBalanceDue" (balanceDueChange)="feeForm.clubRepBalanceDue = $event; clearFeeError()"
            [bFullPaymentRequired]="feeForm.clubRepPhase" (bFullPaymentRequiredChange)="feeForm.clubRepPhase = $event; markFeeDirty()"
            [modifiers]="clubRepModifiers" [phaseNote]="phaseNote('clubRep')" [scope]="'league'"
            hintText="League default for every age group unless an age group or team sets its own. Most-specific wins (never stacked)." />
        }

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
    .settings-grid { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-2); font-size: 0.85rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LeagueDetailComponent implements OnChanges, OnInit, OnDestroy {
  readonly leagueId = input.required<string>();
  readonly saved = output<void>();

  private readonly ladtService = inject(LadtService);
  private readonly jobService = inject(JobService);
  private readonly feeReprice = inject(FeeRepriceService);
  private readonly editGuard = inject(LadtEditGuardService);
  private readonly toast = inject(ToastService);

  /** Set per save() — true when this save flips a payment-phase toggle (either role/direction),
   *  so performSave can fire the quantified success toast on completion. */
  private phaseFlipPending = false;

  private readonly detailForm = viewChild(NgForm);

  /** Unsaved-changes probe — NgForm.dirty covers settings + fee-card controls (all
   *  render inside this form). See LadtEditGuardService. */
  readonly isDirty = (): boolean => this.detailForm()?.dirty ?? false;
  private readonly dirtyProbe = () => this.isDirty();

  /** Flag the form dirty for the phase toggle (a bare checkbox with no ngModel, which NgForm
   *  can't see). Deposit/balance/modifier ngModels dirty the form directly via fee-card's
   *  ControlContainer registration. */
  markFeeDirty(): void {
    this.detailForm()?.form.markAsDirty();
  }

  readonly isTournament = computed(() => this.jobService.currentJob()?.jobTypeId === JOB_TYPE_TOURNAMENT);

  league = signal<LeagueDetailDto | null>(null);
  sports = signal<SportOptionDto[]>([]);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);
  isError = signal(false);

  form: any = {};

  // League is the top tier of the Deposit/BalanceDue cascade (Team -> Agegroup ->
  // League) and carries early-bird/late-fee modifiers.
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
    this.loadSports();
  }

  private loadDetail(): void {
    this.isLoading.set(true);
    this.saveMessage.set(null);

    forkJoin({
      detail: this.ladtService.getLeague(this.leagueId()),
      fees: this.ladtService.getLeagueFees(this.leagueId())
    }).subscribe({
      next: ({ detail, fees }) => {
        this.league.set(detail);
        this.form = { ...detail };
        this.populateFeeForm(fees);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  private populateFeeForm(fees: JobFeeDto[]): void {
    const playerFee = fees.find(f => f.roleId === PLAYER_ROLE);
    const clubRepFee = fees.find(f => f.roleId === CLUBREP_ROLE);

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

  private loadSports(): void {
    if (this.sports().length > 0) return;
    this.ladtService.getSports().subscribe({
      next: (list) => this.sports.set(list)
    });
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
      { leagueId: this.leagueId() },
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

  /** "Keep editing" — collapse the inline confirm and save nothing; stay in the editor. */
  onRepriceCancel(): void {
    this.repriceDialog.set(null);
    this.isSaving.set(false);
  }

  private performSave(repriceExisting: boolean): void {
    this.isSaving.set(true);
    this.saveMessage.set(null);

    const request: UpdateLeagueRequest = {
      leagueName: this.form.leagueName,
      sportId: this.form.sportId,
      bHideContacts: this.form.bHideContacts,
      bHideStandings: this.form.bHideStandings,
      rescheduleEmailsToAddon: this.form.rescheduleEmailsToAddon
    };

    const saves: Observable<any>[] = [
      this.ladtService.updateLeague(this.leagueId(), request)
    ];

    // League-scoped fee rows carry Deposit/BalanceDue (cascade top tier) + modifiers.
    const playerMods = this.toModifierDtos(this.playerModifiers);
    const leagueId = this.leagueId();
    if (this.feeForm.playerDeposit != null || this.feeForm.playerBalanceDue != null
        || this.feeForm.playerPhase != null || playerMods.length > 0) {
      saves.push(this.ladtService.saveFee({
        roleId: PLAYER_ROLE,
        leagueId: leagueId,
        deposit: this.feeForm.playerDeposit,
        balanceDue: this.feeForm.playerBalanceDue,
        bFullPaymentRequired: this.feeForm.playerPhase,
        repriceExisting,
        modifiers: playerMods
      }));
    } else if (this.playerFeeId) {
      saves.push(this.ladtService.deleteFee(this.playerFeeId));
    }

    const clubRepMods = this.toModifierDtos(this.clubRepModifiers);
    if (this.feeForm.clubRepDeposit != null || this.feeForm.clubRepBalanceDue != null
        || this.feeForm.clubRepPhase != null || clubRepMods.length > 0) {
      saves.push(this.ladtService.saveFee({
        roleId: CLUBREP_ROLE,
        leagueId: leagueId,
        deposit: this.feeForm.clubRepDeposit,
        balanceDue: this.feeForm.clubRepBalanceDue,
        bFullPaymentRequired: this.feeForm.clubRepPhase,
        repriceExisting,
        modifiers: clubRepMods
      }));
    } else if (this.clubRepFeeId) {
      saves.push(this.ladtService.deleteFee(this.clubRepFeeId));
    }

    forkJoin(saves).subscribe({
      next: (results) => {
        const updated = results[0] as LeagueDetailDto;
        this.league.set(updated);
        this.form = { ...updated };
        this.isSaving.set(false);
        this.isError.set(false);
        this.saveMessage.set(this.savedMessage(results, 'League saved successfully.'));
        this.captureOriginals();
        const toastMsg = this.feeReprice.saveToastMessage(results, this.phaseFlipPending);
        if (toastMsg) this.toast.show(toastMsg, 'success');
        // TODO: The 'emit' function requires a mandatory void argument
        this.saved.emit();
      },
      error: () => this.isSaving.set(false)
    });
  }

  private savedMessage(results: any[], plain: string): string {
    const who = this.feeReprice.describeReprice(results);
    return who ? `Saved. Repriced ${who}.` : plain;
  }

  private scopeLabel(): string {
    return this.league()?.leagueName || 'this league';
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
   * Read-only phase pointer for the league card. When a league-level fee exists, the card's
   * own toggle + amount-aware explanation own the phase display, so this returns null (no
   * duplicate line). When no fee is set here, phase is managed one tier down — point there
   * (mirrors the "See age group level" fallback in the league grid's Payment Phase column).
   */
  phaseNote(role: 'player' | 'clubRep'): string | null {
    const dep = role === 'player' ? this.feeForm.playerDeposit : this.feeForm.clubRepDeposit;
    const bal = role === 'player' ? this.feeForm.playerBalanceDue : this.feeForm.clubRepBalanceDue;
    if (dep != null || bal != null) return null;
    return 'See age group level.';
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
}
