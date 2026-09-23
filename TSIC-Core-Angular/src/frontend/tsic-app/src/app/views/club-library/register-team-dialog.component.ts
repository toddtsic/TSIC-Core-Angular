import { ChangeDetectionStrategy, Component, OnInit, computed, input, output, signal } from '@angular/core';
import type { AgeGroupDto, ClubTeamDto } from '@core/api';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { LevelOfPlayPickerComponent } from '@shared/teams/level-of-play-picker.component';
import { normalizeLop } from '@shared/teams/lop-choices';
import { EventAgeGroupPickerComponent } from '@views/registration/team/components/event-age-group-picker.component';
import { resolveRecommendedAgeGroupId } from '@views/registration/team/components/event-age-group.util';

/** What the dialog commits: the team plus the two event-level picks. */
export interface RegisterTeamPick {
    team: ClubTeamDto;
    ageGroupId: string;
    levelOfPlay: string;
}

/**
 * "Register <team> for this event" — the library page's counterpart to the wizard
 * fly-in's pinned register sheet. Same two shared pickers, same seeding rules
 * (library LOP if valid; grad-year best-match age group once a level is picked),
 * same honest label (Waitlist vs Register). It is a dialog rather than a sheet
 * because the page has no drawer to dock a sheet to.
 *
 * Owns no domain state: the parent runs the registration call and reloads.
 */
@Component({
    selector: 'app-register-team-dialog',
    standalone: true,
    imports: [TsicDialogComponent, LevelOfPlayPickerComponent, EventAgeGroupPickerComponent],
    template: `
    <tsic-dialog [open]="true" size="md" (requestClose)="cancelled.emit()">
      <div class="modal-content reg-dialog">
        <div class="reg-hero">
          <span class="reg-hero-medallion" aria-hidden="true"><i class="bi bi-trophy-fill"></i></span>
          <span class="reg-hero-id">
            <span class="reg-hero-eyebrow">Registering for {{ eventName() }}</span>
            <span class="reg-hero-name">{{ team().clubTeamName }}</span>
          </span>
          <span class="reg-hero-meta"><span class="reg-meta-key">Grad</span>{{ team().clubTeamGradYear || '—' }}</span>
          <button type="button" class="reg-hero-close" (click)="cancelled.emit()" aria-label="Close">
            <i class="bi bi-x-lg"></i>
          </button>
        </div>

        <div class="reg-body">
          <div class="reg-step">
            <label class="field-label fw-bold">
              <span class="step-num">1</span>
              Event Level of Play
              @if (lopRequired()) { <span class="step-required">Pick one first</span> }
            </label>
            <app-level-of-play-picker
              [selected]="selectedLop()"
              (selectedChange)="onLopSelected($event)" />
          </div>

          <div class="reg-step">
            <label class="field-label fw-bold">
              <span class="step-num">2</span>
              Event Age Group
            </label>
            @if (lopRequired()) {
              <p class="ag-gate-note">
                <i class="bi bi-arrow-up-circle-fill" aria-hidden="true"></i>
                Choose a level of play above &mdash; then this event's age groups unlock.
              </p>
            }
            <app-event-age-group-picker
              variant="chip"
              [ageGroups]="ageGroups()"
              [gradYear]="team().clubTeamGradYear"
              [disabled]="busy() || lopRequired()"
              [showSelectedFee]="true"
              [selected]="selectedAgeGroupId()"
              (selectedChange)="selectedAgeGroupId.set($event)" />
          </div>

          @if (errorMessage()) {
            <div class="alert alert-danger rounded-0 border-0 py-2 px-3 mb-0 small">{{ errorMessage() }}</div>
          }
        </div>

        <div class="reg-actions">
          <button type="button" class="btn-reg-cancel" [disabled]="busy()" (click)="cancelled.emit()">Cancel</button>
          <button type="button" class="btn-reg-submit"
                  [class.is-waitlist]="willWaitlist()"
                  [disabled]="!canSubmit()"
                  (click)="commit()">
            @if (busy()) {
              <span class="spinner-border spinner-border-sm me-1"></span>Registering...
            } @else {
              <i class="bi" aria-hidden="true"
                 [class.bi-hourglass-split]="willWaitlist()"
                 [class.bi-check-lg]="!willWaitlist()"></i>
              <span class="submit-label">{{ submitLabel() }}</span>
            }
          </button>
        </div>
      </div>
    </tsic-dialog>
    `,
    styles: [`
      /* Mirrors the fly-in's register sheet head: medallion / eyebrow + name / meta. */
      .reg-hero {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        background: linear-gradient(to right,
          color-mix(in srgb, var(--bs-primary) 18%, var(--neutral-100)),
          color-mix(in srgb, var(--bs-primary) 12%, var(--neutral-100)) 55%);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-primary) 22%, transparent);
        cursor: grab;
        user-select: none;
        &:active { cursor: grabbing; }
      }

      .reg-hero-medallion {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
        width: 30px;
        height: 30px;
        border-radius: var(--radius-full);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 34%, transparent);
        background: color-mix(in srgb, var(--bs-primary) 14%, var(--brand-surface));
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
      }

      .reg-hero-id { display: flex; flex-direction: column; min-width: 0; flex: 1; }

      .reg-hero-eyebrow {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-bold);
        text-transform: uppercase;
        letter-spacing: 0.1em;
        color: color-mix(in srgb, var(--bs-primary) 70%, var(--brand-text-muted));
      }

      .reg-hero-name {
        font-size: var(--font-size-lg);
        font-weight: var(--font-weight-bold);
        line-height: var(--line-height-tight);
        color: var(--brand-text);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .reg-hero-meta {
        display: inline-flex;
        align-items: baseline;
        gap: var(--space-1);
        flex-shrink: 0;
        padding: 2px var(--space-2);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 22%, transparent);
        border-radius: var(--radius-full);
        background: color-mix(in srgb, var(--brand-surface) 75%, transparent);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        font-variant-numeric: tabular-nums;
      }

      .reg-meta-key {
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--brand-text-muted);
        opacity: 0.8;
      }

      .reg-hero-close {
        border: none;
        background: transparent;
        color: var(--brand-text-muted);
        padding: var(--space-1) var(--space-2);
        line-height: 1;
        border-radius: var(--radius-sm);
        cursor: pointer;
        &:hover { color: var(--brand-text); background: rgba(var(--bs-body-color-rgb), 0.05); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      .reg-body {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4) var(--space-2);
      }

      .reg-step { display: flex; flex-direction: column; gap: var(--space-1); }
      .reg-step + .reg-step {
        padding-top: var(--space-3);
        border-top: 1px solid color-mix(in srgb, var(--bs-primary) 12%, transparent);
      }

      .step-num {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 17px;
        height: 17px;
        margin-right: var(--space-2);
        vertical-align: middle;
        border: 1px solid color-mix(in srgb, var(--bs-primary) 45%, transparent);
        border-radius: var(--radius-full);
        background: color-mix(in srgb, var(--bs-primary) 10%, transparent);
        color: var(--bs-primary);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-bold);
      }

      .step-required {
        margin-left: var(--space-2);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--bs-primary);
      }

      .ag-gate-note {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        margin: 0;
        padding: var(--space-2) var(--space-3);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 30%, transparent);
        border-radius: var(--radius-sm);
        background: color-mix(in srgb, var(--bs-primary) 8%, var(--brand-surface));
        color: var(--brand-text);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        .bi { color: var(--bs-primary); }
      }

      .reg-actions {
        display: flex;
        justify-content: flex-end;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-4);
        border-top: 1px solid var(--border-color);
      }

      /* Token-driven, not Bootstrap .btn-* (precompiled, ignores --bs-primary). */
      .btn-reg-cancel {
        padding: 5px var(--space-3);
        border: 1px solid var(--bs-border-color);
        background: transparent;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        border-radius: var(--radius-sm);
        cursor: pointer;
        transition: background-color 0.12s ease, border-color 0.12s ease, color 0.12s ease;
        &:hover:not(:disabled) {
          color: var(--brand-text);
          border-color: var(--brand-text-muted);
          background: color-mix(in srgb, var(--brand-text-muted) 8%, transparent);
        }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      .btn-reg-submit {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        max-width: 62%;
        padding: 7px var(--space-4);
        border: none;
        border-radius: var(--radius-sm);
        background: var(--bs-primary);
        color: var(--neutral-0);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        box-shadow: var(--shadow-sm);
        cursor: pointer;
        transition: filter 0.12s ease, opacity 0.12s ease;
        &:hover:not(:disabled) { filter: brightness(0.94); }
        &:disabled { opacity: 0.45; cursor: default; }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &.is-waitlist { background: var(--amber-600); }
        .submit-label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      }

      @media (prefers-reduced-motion: reduce) {
        .btn-reg-cancel, .btn-reg-submit { transition: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterTeamDialogComponent implements OnInit {
    readonly team = input.required<ClubTeamDto>();
    readonly eventName = input('this event');
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    readonly busy = input(false);
    /** Server refusal from the last attempt — shown inside the dialog, which stays open. */
    readonly errorMessage = input<string | null>(null);

    readonly confirmed = output<RegisterTeamPick>();
    readonly cancelled = output<void>();

    /** Seeded once in ngOnInit from the team's library level — the dialog is created per team. */
    readonly selectedLop = signal('');
    readonly selectedAgeGroupId = signal('');

    readonly lopRequired = computed(() => !this.selectedLop());

    readonly canSubmit = computed(() =>
        !this.busy() && !!this.selectedAgeGroupId() && !this.lopRequired(),
    );

    readonly willWaitlist = computed(() => {
        const id = this.selectedAgeGroupId();
        if (!id) return false;
        const ag = this.ageGroups().find(a => a.ageGroupId === id);
        if (!ag) return false;
        return ag.ageGroupName.toUpperCase().startsWith('WAITLIST') || ag.registeredCount >= ag.maxTeams;
    });

    readonly submitLabel = computed(() =>
        `${this.willWaitlist() ? 'Waitlist' : 'Register'} ${this.team().clubTeamName}`,
    );

    /**
     * Library LOP seeds the level if it is a valid option; the recommended age group is
     * laid the moment a level exists (never into a disabled picker — see the fly-in's
     * toggleRegister for why). The dialog is created fresh per team, so once is enough.
     */
    ngOnInit(): void {
        const seedLop = normalizeLop(this.team().clubTeamLevelOfPlay || '');
        if (seedLop) {
            this.selectedLop.set(seedLop);
            this.selectedAgeGroupId.set(resolveRecommendedAgeGroupId(this.ageGroups(), this.team().clubTeamGradYear));
        }
    }

    onLopSelected(lop: string): void {
        this.selectedLop.set(lop);
        if (!lop || this.selectedAgeGroupId()) return;
        this.selectedAgeGroupId.set(resolveRecommendedAgeGroupId(this.ageGroups(), this.team().clubTeamGradYear));
    }

    commit(): void {
        if (!this.canSubmit()) return;
        this.confirmed.emit({ team: this.team(), ageGroupId: this.selectedAgeGroupId(), levelOfPlay: this.selectedLop() });
    }
}
