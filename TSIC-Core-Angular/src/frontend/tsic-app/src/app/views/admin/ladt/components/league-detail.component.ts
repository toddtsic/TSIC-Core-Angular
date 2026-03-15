import { ChangeDetectionStrategy, Component, Input, Output, EventEmitter, OnChanges, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LadtService } from '../services/ladt.service';
import type { LeagueDetailDto, UpdateLeagueRequest, SportOptionDto } from '../../../../core/api';

@Component({
  selector: 'app-league-detail',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
        <div class="row g-3">
          <div class="col-md-6">
            <label class="form-label">League Name</label>
            <input class="form-control" [(ngModel)]="form.leagueName" name="leagueName" required>
          </div>
          <div class="col-md-6">
            <label class="form-label">Sport</label>
            <select class="form-select" [(ngModel)]="form.sportId" name="sportId">
              @for (sport of sports(); track sport.sportId) {
                <option [ngValue]="sport.sportId">{{ sport.sportName }}</option>
              }
            </select>
          </div>
        </div>

        <h6 class="section-label mt-4">Settings</h6>
        <div class="row g-3">
          <div class="col-md-6">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideContacts" name="bHideContacts">
              <label class="form-check-label">Hide Contacts</label>
            </div>
          </div>
          <div class="col-md-6">
            <div class="form-check form-switch">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideStandings" name="bHideStandings">
              <label class="form-check-label">Hide Standings</label>
            </div>
          </div>
        </div>

        <h6 class="section-label mt-4">Advanced</h6>
        <div class="row g-3">
          <div class="col-md-6">
            <label class="form-label">Reschedule Emails To (addon)</label>
            <input class="form-control" [(ngModel)]="form.rescheduleEmailsToAddon" name="rescheduleEmailsToAddon">
            <small class="form-text text-body-secondary">Separate emails with semi-colon, no spaces</small>
          </div>
          <div class="col-md-6">
            <label class="form-label">Player Fee Override</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.playerFeeOverride" name="playerFeeOverride">
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
          <div class="alert alert-success mt-3 py-2" role="alert">
            <i class="bi bi-check-circle me-1"></i>{{ saveMessage() }}
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
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LeagueDetailComponent implements OnChanges {
  @Input({ required: true }) leagueId!: string;
  @Output() saved = new EventEmitter<void>();

  private readonly ladtService = inject(LadtService);

  league = signal<LeagueDetailDto | null>(null);
  sports = signal<SportOptionDto[]>([]);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);

  form: any = {};

  ngOnChanges(): void {
    this.loadDetail();
    this.loadSports();
  }

  private loadDetail(): void {
    this.isLoading.set(true);
    this.saveMessage.set(null);

    this.ladtService.getLeague(this.leagueId).subscribe({
      next: (detail) => {
        this.league.set(detail);
        this.form = { ...detail };
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
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
      rescheduleEmailsToAddon: this.form.rescheduleEmailsToAddon,
      playerFeeOverride: this.form.playerFeeOverride
    };

    this.ladtService.updateLeague(this.leagueId, request).subscribe({
      next: (updated) => {
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
