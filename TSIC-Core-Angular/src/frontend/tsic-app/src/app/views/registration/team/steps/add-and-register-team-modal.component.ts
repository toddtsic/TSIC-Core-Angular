import { ChangeDetectionStrategy, Component, DestroyRef, Input, computed, inject, output, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import type { AgeGroupDto } from '@core/api';

/**
 * Combined add-team + event-registration modal — used for the empty-empty
 * teams-step path (rep has zero library teams). Captures team identity
 * (name / grad year / LOP) and event slot (age group) in one form, then
 * chains createClubTeam → registerTeamForEvent. Library entry is created
 * as a side effect; the rep experiences a single "register my team" act.
 *
 * For subsequent registrations (library already populated), the existing
 * flyin → age-group-picker flow is the right tool. This modal is purpose-
 * built for the first-team moment.
 */
@Component({
    selector: 'app-add-and-register-team-modal',
    standalone: true,
    imports: [CurrencyPipe, FormsModule, TsicDialogComponent],
    template: `
    <tsic-dialog [open]="true" size="md" (requestClose)="closed.emit()">
      <div class="modal-content register-modal">

        <!-- Hero -->
        <div class="register-hero">
          <div class="register-hero-eyebrow">
            <span><i class="bi bi-trophy-fill me-1"></i>Register Your First Team</span>
          </div>
          <h5 class="register-hero-title">
            for <span class="register-event-name">{{ eventName }}</span>
          </h5>
          <button type="button" class="register-hero-close" (click)="closed.emit()" aria-label="Close">
            <i class="bi bi-x-lg"></i>
          </button>
        </div>

        <!-- Body -->
        <div class="register-body">

          <!-- Team Name -->
          <div class="form-row">
            <label for="art-name" class="field-label">Team Name</label>
            <input id="art-name" type="text" class="field-input"
                   [value]="teamName()" (input)="teamName.set($any($event.target).value)"
                   placeholder="e.g. 2028 Blue"
                   [class.is-required]="!teamName().trim()"
                   [class.is-invalid]="submitted() && (!teamName().trim() || nameContainsClub())" />
            <div class="wizard-tip">
              Instead of <span class="text-danger fw-semibold">{{ clubName }} 2028 Blue</span>,
              enter <span class="text-success fw-semibold">2028 Blue</span> &mdash; schedules already display your club name.
            </div>
            @if (submitted() && !teamName().trim()) {
              <div class="field-error">Required</div>
            }
            @if (submitted() && teamName().trim() && nameContainsClub()) {
              <div class="field-error">Remove your club name from the team name.</div>
            }
            @if (!submitted() && nameContainsClub()) {
              <div class="field-error" style="color: var(--bs-warning)">
                <i class="bi bi-exclamation-triangle me-1"></i>Contains your club name &mdash; please remove it.
              </div>
            }
          </div>

          <!-- Grad Year -->
          <div class="form-row form-row-split">
            <div>
              <label for="art-year" class="field-label">Grad Year</label>
              <select id="art-year" class="field-select"
                      [ngModel]="gradYear()" (ngModelChange)="gradYear.set($event)"
                      [class.is-required]="!gradYear()"
                      [class.is-invalid]="submitted() && !gradYear()">
                <option value="">Select</option>
                @for (yr of gradYearOptions; track yr) {
                  <option [value]="yr">{{ yr === 'Adult' ? 'Adult Team' : yr }}</option>
                }
              </select>
              @if (submitted() && !gradYear()) {
                <div class="field-error">Required</div>
              }
            </div>

            <div>
              <label class="field-label">Level of Play</label>
              <div class="lop-pills" role="radiogroup" aria-label="Level of play">
                @for (lop of lopChoices; track lop.value) {
                  <button type="button" class="lop-pill" role="radio"
                          [class.active]="levelOfPlay() === lop.value"
                          [class.is-invalid]="submitted() && !levelOfPlay()"
                          [attr.aria-checked]="levelOfPlay() === lop.value"
                          [attr.title]="lop.label"
                          (click)="levelOfPlay.set(lop.value)">
                    {{ lop.short }}
                  </button>
                }
              </div>
              @if (submitted() && !levelOfPlay()) {
                <div class="field-error">Required</div>
              }
            </div>
          </div>

          <!-- Age Group section — gated until team identity is filled -->
          <div class="age-section" [class.is-locked]="!stage4Ready()">
            <label class="field-label age-section-label">
              Age Group for <span class="age-section-event">{{ eventName }}</span>
            </label>

            @if (!stage4Ready()) {
              <div class="age-locked-tip">
                <i class="bi bi-arrow-up-circle"></i>
                Fill in the team details above to choose an age group.
              </div>
            } @else {
              <p class="wizard-tip age-tip">
                Tap a card to register for that age group.
                @if (hasRecommended()) {
                  <span class="age-legend"><i class="bi bi-star-fill"></i> = best match for this team</span>
                }
              </p>
            }

            <div class="age-pill-grid" role="radiogroup">
              @for (ag of pills(); track ag.ageGroupId; let i = $index) {
                <button type="button" class="age-pill" role="radio"
                        [class.is-recommended]="ag.isRecommended"
                        [class.is-selected]="selectedAgeGroup() === ag.ageGroupId"
                        [class.is-full]="ag.isFull"
                        [class.is-almost-full]="ag.isAlmostFull && !ag.isFull"
                        [disabled]="!stage4Ready()"
                        [attr.aria-checked]="selectedAgeGroup() === ag.ageGroupId"
                        [style.animation-delay]="stage4Ready() ? (60 + (i * 40)) + 'ms' : '0ms'"
                        (click)="selectedAgeGroup.set(ag.ageGroupId)">
                  <span class="age-pill-name">
                    {{ ag.ageGroupName }}
                    @if (ag.isRecommended) { <i class="bi bi-star-fill age-pill-star"></i> }
                    @if (selectedAgeGroup() === ag.ageGroupId) { <i class="bi bi-check-circle-fill age-pill-check"></i> }
                  </span>
                  <span class="age-pill-fee">{{ ag.fee | currency }}</span>
                  <span class="age-pill-spots"
                        [class.text-warning]="ag.isAlmostFull && !ag.isFull"
                        [class.text-danger]="ag.isFull">
                    @if (ag.isFull) { <i class="bi bi-exclamation-circle me-1"></i>Waitlist }
                    @else { {{ ag.spotsLeft }} {{ ag.spotsLeft === 1 ? 'spot' : 'spots' }} }
                  </span>
                </button>
              }
            </div>
            @if (submitted() && stage4Ready() && !selectedAgeGroup()) {
              <div class="field-error" style="text-align: center; margin-top: var(--space-2)">
                Pick an age group to register your team.
              </div>
            }
          </div>

          @if (errorMsg()) {
            <div class="alert alert-danger rounded-0 border-0 py-2 px-3 mb-0 small">{{ errorMsg() }}</div>
          }

          <!-- Library aside — value prop, not lecture -->
          <div class="library-aside">
            <i class="bi bi-shield-check"></i>
            <span>
              We'll save this team to your <strong>Club Library</strong> too &mdash;
              register it again in any future TSIC event with one click.
            </span>
          </div>
        </div>

        <!-- Footer -->
        <div class="register-footer">
          <button type="button" class="btn btn-sm btn-outline-secondary" (click)="closed.emit()">Cancel</button>
          <button type="button" class="btn btn-success fw-semibold register-cta"
                  (click)="save()"
                  [disabled]="saving() || !canSubmit()"
                  [attr.title]="canSubmit() ? null : disabledReason()">
            @if (saving()) {
              <span class="spinner-border spinner-border-sm me-2"></span>Registering...
            } @else {
              <i class="bi bi-trophy-fill me-2"></i>
              Register Team for this Event
            }
          </button>
        </div>
      </div>
    </tsic-dialog>
  `,
    styles: [`
      /* ── Hero ──────────────────────────────────────────────────────── */
      .register-hero {
        position: relative;
        padding: var(--space-3) var(--space-4) var(--space-3);
        background: linear-gradient(135deg,
          rgba(var(--bs-success-rgb), 0.1) 0%,
          rgba(var(--bs-success-rgb), 0.02) 100%);
        border-bottom: 2px solid rgba(var(--bs-success-rgb), 0.18);
        text-align: center;
      }

      .register-hero-eyebrow {
        display: inline-flex;
        align-items: center;
        gap: var(--space-3);
        font-size: 11px;
        font-weight: var(--font-weight-bold);
        letter-spacing: 0.1em;
        text-transform: uppercase;
        color: var(--bs-success);
        margin-bottom: var(--space-1);

        &::before, &::after {
          content: '';
          display: block;
          width: 32px;
          height: 1px;
        }
        &::before { background: linear-gradient(to right, transparent, rgba(var(--bs-success-rgb), 0.45)); }
        &::after  { background: linear-gradient(to left,  transparent, rgba(var(--bs-success-rgb), 0.45)); }

        > span { display: inline-flex; align-items: center; gap: var(--space-1); }
      }

      .register-hero-title {
        margin: 0;
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
      }

      .register-event-name {
        font-weight: var(--font-weight-bold);
        color: var(--bs-success);
      }

      .register-hero-close {
        position: absolute;
        top: var(--space-2);
        right: var(--space-2);
        border: none;
        background: transparent;
        color: var(--brand-text-muted);
        padding: var(--space-1) var(--space-2);
        line-height: 1;
        border-radius: var(--radius-sm);
        cursor: pointer;
        transition: background-color 0.15s ease, color 0.15s ease;

        &:hover { color: var(--brand-text); background: rgba(var(--bs-body-color-rgb), 0.05); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      /* ── Body ──────────────────────────────────────────────────────── */
      .register-body { padding: var(--space-3) var(--space-3) var(--space-1); }

      .form-row + .form-row,
      .form-row + .age-section,
      .age-section + .library-aside,
      .form-row + .library-aside {
        margin-top: var(--space-3);
      }

      .form-row-split {
        display: grid;
        grid-template-columns: 1fr 1.4fr;
        gap: var(--space-3);
      }

      @media (max-width: 480px) {
        .form-row-split { grid-template-columns: 1fr; }
      }

      /* ── LOP pills (compact for the split row) ──────────────────────── */
      .lop-pills {
        display: flex;
        gap: var(--space-1);
      }

      .lop-pill {
        flex: 1 1 0;
        min-width: 36px;
        padding: var(--space-1) var(--space-1);
        border: 1.5px solid var(--border-color);
        border-radius: var(--radius-full);
        background: var(--brand-surface);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        cursor: pointer;
        transition: all 0.12s ease;
        text-align: center;

        &:hover { border-color: var(--bs-primary); }

        &.active {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.12);
          color: var(--bs-primary);
        }

        &.is-invalid:not(.active) {
          border-color: rgba(var(--bs-danger-rgb), 0.5);
        }

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      /* ── Age section ───────────────────────────────────────────────── */
      .age-section {
        padding: var(--space-3);
        border: 2px dashed var(--border-color);
        border-radius: var(--radius-md);
        background: rgba(var(--bs-success-rgb), 0.025);
        transition: border-color 0.2s ease, background 0.2s ease;

        &.is-locked {
          background: rgba(var(--bs-dark-rgb), 0.025);
          border-style: dashed;
        }

        &:not(.is-locked) {
          border-color: rgba(var(--bs-success-rgb), 0.35);
          border-style: solid;
        }
      }

      .age-section-label {
        display: block;
        margin-bottom: var(--space-2);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        text-align: center;
      }

      .age-section-event {
        color: var(--bs-success);
        font-weight: var(--font-weight-bold);
      }

      .age-locked-tip {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: var(--space-2);
        padding: var(--space-3) var(--space-2);
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
        font-style: italic;

        i { color: var(--bs-primary); font-size: var(--font-size-base); }
      }

      .age-tip {
        text-align: center;
        margin: 0 0 var(--space-2);
      }

      .age-legend {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        margin-left: var(--space-2);
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);

        i { font-size: 8px; color: var(--bs-danger); }
      }

      .age-pill-grid {
        display: flex;
        flex-wrap: wrap;
        justify-content: center;
        gap: var(--space-2);
      }

      .age-pill {
        position: relative;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 2px;
        min-width: 108px;
        min-height: 60px;
        padding: var(--space-2) var(--space-3);
        border: 2px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        cursor: pointer;
        box-shadow: var(--shadow-xs);
        transition: border-color 0.15s ease, background 0.15s ease,
                    box-shadow 0.15s ease, transform 0.15s ease;
        animation: agePillFadeIn 0.3s ease-out backwards;

        &:hover:not(:disabled) {
          border-color: var(--bs-success);
          background: rgba(var(--bs-success-rgb), 0.06);
          transform: translateY(-2px);
          box-shadow: var(--shadow-sm);
        }

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:active:not(:disabled) { transform: scale(0.97); }

        &.is-selected {
          border-color: var(--bs-success);
          background: rgba(var(--bs-success-rgb), 0.1);
          box-shadow: 0 0 0 2px rgba(var(--bs-success-rgb), 0.2), var(--shadow-xs);
        }

        &.is-full {
          border-color: rgba(var(--bs-warning-rgb), 0.4);
          background: rgba(var(--bs-warning-rgb), 0.04);
        }

        &:hover:not(:disabled).is-full {
          border-color: var(--bs-warning);
          background: rgba(var(--bs-warning-rgb), 0.1);
        }

        &.is-almost-full .age-pill-spots {
          font-weight: var(--font-weight-semibold);
        }

        &:disabled {
          opacity: 0.42;
          cursor: not-allowed;
          animation: none;
        }
      }

      .age-pill-name {
        font-weight: var(--font-weight-bold);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        display: inline-flex;
        align-items: center;
        gap: 4px;
      }

      .age-pill-star { font-size: 9px; color: var(--bs-danger); }
      .age-pill-check { font-size: 11px; color: var(--bs-success); }

      .age-pill-fee {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
      }

      .age-pill-spots {
        font-size: 10px;
        color: var(--brand-text-muted);
        white-space: nowrap;
      }

      /* ── Library aside ─────────────────────────────────────────────── */
      .library-aside {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        margin-top: var(--space-3);
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-primary-rgb), 0.04);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.12);
        border-radius: var(--radius-sm);
        font-size: var(--font-size-xs);
        color: var(--brand-text);
        line-height: var(--line-height-normal);

        i {
          color: var(--bs-primary);
          font-size: var(--font-size-base);
          flex-shrink: 0;
          margin-top: 1px;
        }

        strong { color: var(--brand-text); }
      }

      /* ── Footer ────────────────────────────────────────────────────── */
      .register-footer {
        display: flex;
        justify-content: flex-end;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border-top: 1px solid var(--border-color);
      }

      .register-cta {
        padding: var(--space-2) var(--space-4);
        font-size: var(--font-size-base);
      }

      /* ── Animations ────────────────────────────────────────────────── */
      @keyframes agePillFadeIn {
        from { opacity: 0; transform: translateY(6px); }
        to   { opacity: 1; transform: translateY(0); }
      }

      @media (prefers-reduced-motion: reduce) {
        .age-pill { animation: none !important; transition: none; }
        .age-pill:hover:not(:disabled) { transform: none; }
        .age-section { transition: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddAndRegisterTeamModalComponent {
    @Input() clubName = '';
    @Input() eventName = '';
    @Input() ageGroups: AgeGroupDto[] = [];

    readonly saved = output<void>();
    readonly closed = output<void>();

    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    /** Grad year options: current year through +12, plus Adult. */
    readonly gradYearOptions: string[] = (() => {
        const now = new Date().getFullYear();
        const years: string[] = [];
        for (let y = now; y <= now + 12; y++) years.push(String(y));
        years.push('Adult');
        return years;
    })();

    /**
     * LOP choices — short label for the compact pill, full label for the title attr.
     * Matches the team-form-modal palette so reps see the same taxonomy.
     */
    readonly lopChoices: ReadonlyArray<{ value: string; short: string; label: string }> = [
        { value: '1', short: '1', label: '1 (weakest)' },
        { value: '2', short: '2', label: '2' },
        { value: '3', short: '3', label: '3' },
        { value: '4', short: '4', label: '4' },
        { value: '5', short: '5', label: '5 (strongest)' },
    ];

    readonly teamName = signal('');
    readonly gradYear = signal('');
    readonly levelOfPlay = signal('');
    readonly selectedAgeGroup = signal('');
    readonly submitted = signal(false);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);

    /** True when the team name contains the club name (case-insensitive). */
    readonly nameContainsClub = computed(() => {
        const club = this.clubName.trim().toLowerCase();
        const name = this.teamName().trim().toLowerCase();
        return club.length > 0 && name.length > 0 && name.includes(club);
    });

    /** Age picker is unlocked once the team identity is fully and validly captured. */
    readonly stage4Ready = computed(() =>
        this.teamName().trim().length > 0 &&
        this.gradYear().length > 0 &&
        this.levelOfPlay().length > 0 &&
        !this.nameContainsClub(),
    );

    /** Submit gate — every field must be present AND an age group selected. */
    readonly canSubmit = computed(() => this.stage4Ready() && !!this.selectedAgeGroup());

    /** Tooltip naming what's still needed when the submit button is disabled. */
    readonly disabledReason = computed(() => {
        if (!this.teamName().trim()) return 'Enter a team name';
        if (this.nameContainsClub())  return 'Remove the club name from the team name';
        if (!this.gradYear())         return 'Pick a grad year';
        if (!this.levelOfPlay())      return 'Pick a level of play';
        if (!this.selectedAgeGroup()) return 'Pick an age group';
        return '';
    });

    /** Age-group cards with derived presentation flags (mirrors age-group-picker-modal). */
    readonly pills = computed(() => {
        const recommended = this.bestMatch();
        return this.ageGroups.map(ag => {
            const spotsLeft = Math.max(0, ag.maxTeams - ag.registeredCount);
            return {
                ageGroupId: ag.ageGroupId,
                ageGroupName: ag.ageGroupName,
                fee: (ag.deposit || 0) + (ag.balanceDue || 0),
                spotsLeft,
                isFull: spotsLeft === 0,
                isAlmostFull: spotsLeft > 0 && spotsLeft <= 2,
                isRecommended: ag.ageGroupId === recommended,
            };
        });
    });

    readonly hasRecommended = computed(() => this.pills().some(p => p.isRecommended));

    save(): void {
        this.submitted.set(true);
        this.errorMsg.set(null);

        if (!this.teamName().trim() || !this.gradYear() || !this.levelOfPlay() || this.nameContainsClub()) return;
        if (!this.selectedAgeGroup()) return;

        this.saving.set(true);

        const teamName = this.teamName().trim();
        const gradYear = this.gradYear();
        const lop = this.levelOfPlay().trim();
        const ageGroupId = this.selectedAgeGroup();

        this.teamReg.createClubTeam({
            clubTeamName: teamName,
            clubTeamGradYear: gradYear,
            levelOfPlay: lop || undefined,
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (newTeam) => {
                    this.teamReg.registerTeamForEvent({
                        clubTeamId: newTeam.clubTeamId,
                        ageGroupId,
                        teamName: newTeam.clubTeamName,
                        clubTeamGradYear: newTeam.clubTeamGradYear,
                        levelOfPlay: newTeam.clubTeamLevelOfPlay || lop,
                    })
                        .pipe(takeUntilDestroyed(this.destroyRef))
                        .subscribe({
                            next: (regResp) => {
                                this.saving.set(false);
                                if (!regResp.success) {
                                    // Library entry was created but registration was rejected —
                                    // keep the team in library, surface the error, let the rep
                                    // close and pick again from the library flow.
                                    this.errorMsg.set(
                                        (regResp.message ?? 'Registration was not accepted.') +
                                        ' Your team is saved in your library; you can register it from there.',
                                    );
                                    return;
                                }
                                const msg = regResp.isWaitlisted
                                    ? `${teamName} waitlisted for ${regResp.waitlistAgegroupName ?? ''}`
                                    : `${teamName} registered for the event!`;
                                this.toast.show(msg, regResp.isWaitlisted ? 'warning' : 'success', 3000);
                                this.saved.emit();
                            },
                            error: () => {
                                this.saving.set(false);
                                this.errorMsg.set(
                                    'Your team is saved in your library, but the event registration failed. ' +
                                    'Close this dialog and try registering it from the library.',
                                );
                            },
                        });
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Failed to create team.');
                },
            });
    }

    /** Pick the age group whose name matches the team's grad year (best discoverability hint). */
    private bestMatch(): string {
        const yr = this.gradYear();
        if (!yr || !this.ageGroups.length) return '';
        const exact = this.ageGroups.find(ag => ag.ageGroupName === yr);
        if (exact) return exact.ageGroupId;
        const contains = this.ageGroups.find(ag => ag.ageGroupName.includes(yr));
        if (contains) return contains.ageGroupId;
        return '';
    }
}
