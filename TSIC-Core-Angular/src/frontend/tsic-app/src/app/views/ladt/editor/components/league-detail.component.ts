import { ChangeDetectionStrategy, Component, Output, EventEmitter, OnChanges, computed, signal, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { LadtService } from '../services/ladt.service';
import { FeeCardComponent, type ModifierForm } from './fee-card.component';
import { JobService } from '../../../../infrastructure/services/job.service';
import type { LeagueDetailDto, UpdateLeagueRequest, SportOptionDto, JobFeeDto, FeeModifierDto } from '../../../../core/api';

const PLAYER_ROLE = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
const CLUBREP_ROLE = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';
const JOB_TYPE_TOURNAMENT = 2;

@Component({
  selector: 'app-league-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, FeeCardComponent],
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
            namePrefix="clubRep" [(deposit)]="feeForm.clubRepDeposit"
            [(balanceDue)]="feeForm.clubRepBalanceDue" [modifiers]="clubRepModifiers"
            hintText="League default for every age group unless an age group or team sets its own. Most-specific wins (never stacked)." />
          <app-fee-card header="Player — League Fees" headerIcon="bi-person" variant="player"
            namePrefix="player" [(deposit)]="feeForm.playerDeposit"
            [(balanceDue)]="feeForm.playerBalanceDue" [modifiers]="playerModifiers" placeholder="Optional"
            hintText="League default for every age group unless an age group or team sets its own. Most-specific wins (never stacked)." />
        } @else {
          <app-fee-card header="Player — League Fees" headerIcon="bi-person" variant="player"
            namePrefix="player" [(deposit)]="feeForm.playerDeposit"
            [(balanceDue)]="feeForm.playerBalanceDue" [modifiers]="playerModifiers" placeholder="Optional"
            hintText="League default for every age group unless an age group or team sets its own. Most-specific wins (never stacked)." />
          <app-fee-card header="Club Rep / Team — League Fees" headerIcon="bi-shield" variant="clubrep"
            namePrefix="clubRep" [(deposit)]="feeForm.clubRepDeposit"
            [(balanceDue)]="feeForm.clubRepBalanceDue" [modifiers]="clubRepModifiers"
            hintText="League default for every age group unless an age group or team sets its own. Most-specific wins (never stacked)." />
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
            <span class="small text-success">
              <i class="bi bi-check-circle me-1"></i>{{ saveMessage() }}
            </span>
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
export class LeagueDetailComponent implements OnChanges {
  readonly leagueId = input.required<string>();
  @Output() saved = new EventEmitter<void>();

  private readonly ladtService = inject(LadtService);
  private readonly jobService = inject(JobService);

  readonly isTournament = computed(() => this.jobService.currentJob()?.jobTypeId === JOB_TYPE_TOURNAMENT);

  league = signal<LeagueDetailDto | null>(null);
  sports = signal<SportOptionDto[]>([]);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);

  form: any = {};

  // League is the top tier of the Deposit/BalanceDue cascade (Team -> Agegroup ->
  // League) and carries early-bird/late-fee modifiers.
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
      clubRepDeposit: clubRepFee?.deposit ?? null,
      clubRepBalanceDue: clubRepFee?.balanceDue ?? null
    };

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

  private loadSports(): void {
    if (this.sports().length > 0) return;
    this.ladtService.getSports().subscribe({
      next: (list) => this.sports.set(list)
    });
  }

  save(): void {
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
        || playerMods.length > 0) {
      saves.push(this.ladtService.saveFee({
        roleId: PLAYER_ROLE,
        leagueId: leagueId,
        deposit: this.feeForm.playerDeposit,
        balanceDue: this.feeForm.playerBalanceDue,
        modifiers: playerMods
      }));
    } else if (this.playerFeeId) {
      saves.push(this.ladtService.deleteFee(this.playerFeeId));
    }

    const clubRepMods = this.toModifierDtos(this.clubRepModifiers);
    if (this.feeForm.clubRepDeposit != null || this.feeForm.clubRepBalanceDue != null
        || clubRepMods.length > 0) {
      saves.push(this.ladtService.saveFee({
        roleId: CLUBREP_ROLE,
        leagueId: leagueId,
        deposit: this.feeForm.clubRepDeposit,
        balanceDue: this.feeForm.clubRepBalanceDue,
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
        this.saveMessage.set('League saved successfully.');
        this.saved.emit();
      },
      error: () => this.isSaving.set(false)
    });
  }
}
