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
      rescheduleEmailsToAddon: this.form.rescheduleEmailsToAddon
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
