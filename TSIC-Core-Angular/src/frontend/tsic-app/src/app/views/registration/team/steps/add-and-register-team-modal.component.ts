import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, output, signal, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import type { AgeGroupDto, ClubTeamDto } from '@core/api';
import { LevelOfPlayPickerComponent } from '@shared/teams/level-of-play-picker.component';
import { clubNameInTeamName, isBareYearName } from '@shared/teams/team-name-hints';
import { TeamNameSchedulePreviewComponent } from '@shared/teams/team-name-schedule-preview.component';
import { EventAgeGroupPickerComponent } from '@views/registration/team/components/event-age-group-picker.component';
import { resolveRecommendedAgeGroupId } from '@views/registration/team/components/event-age-group.util';
import { extractHttpErrorMessage } from '@infrastructure/interceptors/http-error-utils';

/**
 * Combined add-team + event-registration modal — THE way a new team enters the
 * wizard while registration is open. Captures team identity (name / grad year /
 * LOP) and event slot (age group) in one form, then chains createClubTeam →
 * registerTeamForEvent. The library entry is a side effect; the rep experiences
 * a single "register my team" act.
 *
 * It used to serve only the first-ever team (empty library); every later add
 * went through the plain "Add to Library" form, whose save left the team in the
 * library UNREGISTERED with a toast that said "added". Reps closed the drawer
 * believing the team was in the event. Now every add opens this modal, and the
 * library-only path survives as an explicitly labelled secondary (ruling: Todd,
 * 2026-09-22) rather than the default outcome of the primary button.
 *
 * Registering a team that already exists in the library is NOT this modal's
 * job — the flyin's Register button on the row is.
 */
@Component({
    selector: 'app-add-and-register-team-modal',
    standalone: true,
    imports: [FormsModule, TsicDialogComponent, LevelOfPlayPickerComponent, TeamNameSchedulePreviewComponent, EventAgeGroupPickerComponent],
    template: `
    <tsic-dialog [open]="true" size="md" (requestClose)="closed.emit()">
      <div class="modal-content register-modal">

        <!-- Hero -->
        <div class="register-hero">
          <div class="register-hero-eyebrow">
            <span><i class="bi bi-trophy-fill me-1"></i>{{ firstTeam() ? 'Register Your First Team' : 'Add & Register a Team' }}</span>
          </div>
          <h5 class="register-hero-title">
            for <span class="register-event-name">{{ eventName() }}</span>
          </h5>
          <button type="button" class="register-hero-close" (click)="closed.emit()" aria-label="Close">
            <i class="bi bi-x-lg"></i>
          </button>
        </div>

        <!-- Body -->
        <div class="register-body">

          <!-- ── Step 1 — Name your team ─────────────────────────── -->
          <div class="step-section"
               role="group" aria-labelledby="step-1-title"
               [class.is-active]="activeStep() === 1"
               [class.is-completed]="step1Done()">
            <div class="step-eyebrow">
              <span class="step-circle"
                    [class.is-active]="activeStep() === 1"
                    [class.is-completed]="step1Done()">
                @if (step1Done()) { <i class="bi bi-check-lg"></i> } @else { 1 }
              </span>
              <span class="step-title" id="step-1-title">Name your team</span>
            </div>

            <input id="art-name" type="text" class="field-input"
                   [value]="teamName()" (input)="teamName.set($any($event.target).value)"
                   placeholder="e.g. 2028 Blue"
                   [class.is-required]="!teamName().trim()"
                   [class.is-invalid]="submitted() && (!teamName().trim() || nameContainsClub() || nameIsDuplicate())" />
            <!-- The club-name hammer: the live schedule label, red when the club name is in it. -->
            <team-name-schedule-preview [clubName]="clubName()" [teamName]="teamName()" />
            @if (submitted() && !teamName().trim()) {
              <div class="field-error">Required</div>
            }
            @if (nameIsDuplicate()) {
              <div class="field-error">
                <i class="bi bi-exclamation-triangle me-1"></i>
                <strong>{{ teamName().trim() }}</strong> is already in your library &mdash; register it from the library list instead.
              </div>
            }
            <!-- Soft nudge, never a block: a bare year is legal, but two squads in one
                 grad year become indistinguishable and the rep's own grid reads
                 Team 2028 | Grad 2028. -->
            @if (nameIsBareYear() && !nameIsDuplicate()) {
              <div class="name-nudge">
                <i class="bi bi-lightbulb" aria-hidden="true"></i>
                <span>Just a year? Add a word so you can tell your teams apart later &mdash;
                  <strong>{{ teamName().trim() }} Blue</strong>, <strong>{{ teamName().trim() }} Elite</strong>. Optional.</span>
              </div>
            }
          </div>

          <!-- ── Step 2 — Team details ───────────────────────────── -->
          <div class="step-section"
               role="group" aria-labelledby="step-2-title"
               [class.is-active]="activeStep() === 2"
               [class.is-completed]="step2Done()"
               [class.is-locked]="!step1Done()">
            <div class="step-eyebrow">
              <span class="step-circle"
                    [class.is-active]="activeStep() === 2"
                    [class.is-completed]="step2Done()">
                @if (step2Done()) { <i class="bi bi-check-lg"></i> } @else { 2 }
              </span>
              <span class="step-title" id="step-2-title">Team details</span>
            </div>

            <div class="form-row-split">
              <div>
                <label for="art-year" class="field-label">Grad Year</label>
                <select id="art-year" class="field-select"
                        [ngModel]="gradYear()" (ngModelChange)="gradYear.set($event)"
                        [disabled]="!step1Done()"
                        [class.is-required]="!gradYear()"
                        [class.is-invalid]="submitted() && !gradYear()">
                  <option value="">Select</option>
                  @for (yr of gradYearOptions; track yr) {
                    <option [value]="yr">{{ yr === 'Adult' ? 'Adult Team' : yr }}</option>
                  }
                </select>
                <div class="grad-year-tip">
                  Grad year of the <strong>majority</strong> of your players &mdash;
                  <em>not</em> an age group. Helps suggest the right age group at registration.
                </div>
                @if (submitted() && !gradYear()) {
                  <div class="field-error">Required</div>
                }
              </div>

              <div>
                <label class="field-label">Level of Play</label>
                <app-level-of-play-picker
                  [fill]="true"
                  [disabled]="!step1Done()"
                  [invalid]="submitted() && !levelOfPlay()"
                  [selected]="levelOfPlay()"
                  (selectedChange)="levelOfPlay.set($event)" />
                @if (submitted() && !levelOfPlay()) {
                  <div class="field-error">Required</div>
                }
              </div>
            </div>
          </div>

          <!-- ── Step 3 — Age group ──────────────────────────────── -->
          <div class="step-section"
               role="group" aria-labelledby="step-3-title"
               [class.is-active]="activeStep() === 3 && !selectedAgeGroup()"
               [class.is-completed]="!!selectedAgeGroup()"
               [class.is-locked]="!stage4Ready()">
            <div class="step-eyebrow">
              <span class="step-circle"
                    [class.is-active]="activeStep() === 3 && !selectedAgeGroup()"
                    [class.is-completed]="!!selectedAgeGroup()">
                @if (selectedAgeGroup()) { <i class="bi bi-check-lg"></i> } @else { 3 }
              </span>
              <span class="step-title" id="step-3-title">
                Age Group for <span class="step-title-event">{{ eventName() }}</span>
              </span>
            </div>

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

            <app-event-age-group-picker
              variant="pill"
              [ageGroups]="ageGroups()"
              [gradYear]="gradYear()"
              [disabled]="!stage4Ready()"
              [selected]="selectedAgeGroup()"
              (selectedChange)="selectedAgeGroup.set($event)" />
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
              We'll save this team to your <strong>Club Team Library</strong> too &mdash;
              register it again in any future TSIC event with one click.
            </span>
          </div>
        </div>

        <!-- Footer — one primary outcome (registered) and one explicitly labelled
             secondary (library only). The secondary names its consequence on the
             button itself, because "Add to Library" alone is what reps read as
             "done" before. -->
        <div class="register-footer">
          <button type="button" class="btn btn-sm btn-outline-secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</button>
          <button type="button" class="btn btn-sm btn-outline-secondary library-only-cta"
                  (click)="saveLibraryOnly()"
                  [disabled]="saving() || !stage4Ready()"
                  [attr.title]="stage4Ready() ? null : disabledReason()">
            @if (savingLibraryOnly()) {
              <span class="spinner-border spinner-border-sm me-1"></span>Saving...
            } @else {
              <span class="library-only-label">Save to library only</span>
              <span class="library-only-sub">NOT registered for {{ eventName() }}</span>
            }
          </button>
          <button type="button" class="btn btn-success fw-semibold register-cta"
                  (click)="save()"
                  [disabled]="saving() || !canSubmit()"
                  [attr.title]="canSubmit() ? null : disabledReason()">
            @if (saving() && !savingLibraryOnly()) {
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

      /* Stepwise pattern (.step-section / .step-circle / .step-eyebrow)
         lives in styles/_stepwise-region.scss — shared with team-form-modal. */

      .form-row-split {
        display: grid;
        grid-template-columns: 1fr 1.4fr;
        gap: var(--space-3);
      }

      @media (max-width: 480px) {
        .form-row-split { grid-template-columns: 1fr; }
      }

      /* Grad-year disambiguation: prevents users from picking the AG's
         grad year here. Lives under the select in the narrow split column. */
      .grad-year-tip {
        margin-top: var(--space-1);
        font-size: var(--font-size-xs);
        line-height: var(--line-height-normal);
        color: var(--brand-text-muted);
      }
      .grad-year-tip strong { color: var(--brand-text); }
      .grad-year-tip em { color: var(--bs-danger); font-style: normal; font-weight: var(--font-weight-semibold); }

      /* LOP pills render via <app-level-of-play-picker [fill]="true">, which
         owns its own .lop-pill styles (fill variant = split-row stretch). */

      /* ── Age section inner ─────────────────────────────────────────── */
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

      /* The age-group cards render via <app-event-age-group-picker
         variant="pill">, which owns its own .age-pill styles + fade-in. */

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

      /* ── Bare-year nudge ───────────────────────────────────────────────
         Advisory, so it takes the muted tip tone, not .field-error's danger. The
         bulb + primary tint says "suggestion"; it must never read as a validation
         failure or the rep will assume the button is locked. */
      .name-nudge {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        margin-top: var(--space-1);
        padding: var(--space-1) var(--space-2);
        border-left: 3px solid color-mix(in srgb, var(--bs-primary) 45%, transparent);
        background: color-mix(in srgb, var(--bs-primary) 5%, transparent);
        font-size: var(--font-size-xs);
        line-height: var(--line-height-normal);
        color: var(--brand-text);

        i { color: var(--bs-primary); flex-shrink: 0; margin-top: 1px; }
        strong { font-weight: var(--font-weight-semibold); }
      }

      /* ── Footer ────────────────────────────────────────────────────── */
      .register-footer {
        display: flex;
        justify-content: flex-end;
        align-items: stretch;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border-top: 1px solid var(--border-color);
      }

      .register-cta {
        padding: var(--space-2) var(--space-4);
        font-size: var(--font-size-base);
      }

      /* Two-line secondary: the verb on top, the consequence underneath in
         warning tone. Pushed left of the primary so the eye reads the pair as
         "quiet option / real action", and never mistakes it for Cancel. */
      .library-only-cta {
        display: inline-flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 1px;
        margin-left: auto;
        line-height: 1.2;
        text-align: center;
      }

      .library-only-label {
        font-weight: var(--font-weight-semibold);
      }

      .library-only-sub {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--bs-warning-text-emphasis);
      }

      .library-only-cta:disabled .library-only-sub { opacity: 0.7; }

      @media (max-width: 480px) {
        .register-footer { flex-wrap: wrap; }
        .library-only-cta { margin-left: 0; flex: 1 1 100%; order: 3; }
        .register-cta { flex: 1 1 auto; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddAndRegisterTeamModalComponent {
    readonly clubName = input('');
    readonly eventName = input('');
    readonly ageGroups = input<AgeGroupDto[]>([]);
    /** Empty library → "Register Your First Team" hero; otherwise "Add & Register a Team". */
    readonly firstTeam = input(false);
    /** Existing library teams — blocks a duplicate name (case-insensitive). The server
     *  refuses the same collision; catching it here keeps the rep in the form. */
    readonly existingTeams = input<readonly ClubTeamDto[]>([]);

    /** Team created AND registered for this event. */
    readonly saved = output<void>();
    /** Team created in the library only — the secondary path. Carries the new row so the
     *  parent can track it as "not yet registered" until the rep either registers it or
     *  knowingly leaves it. */
    readonly savedLibraryOnly = output<ClubTeamDto>();
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

    readonly teamName = signal('');
    readonly gradYear = signal('');
    readonly levelOfPlay = signal('');
    readonly selectedAgeGroup = signal('');
    readonly submitted = signal(false);
    readonly saving = signal(false);
    /** Which button is in flight — both share `saving` so neither can double-submit. */
    readonly savingLibraryOnly = signal(false);
    readonly errorMsg = signal<string | null>(null);

    /** The whole club name is in the team name — blocks the save; the preview says why. */
    readonly nameContainsClub = computed(() => clubNameInTeamName(this.clubName(), this.teamName()) === 'full');

    /** True when the name matches a library team (case-insensitive). Same rule as team-form-modal. */
    readonly nameIsDuplicate = computed(() => {
        const name = this.teamName().trim().toLowerCase();
        if (!name) return false;
        return this.existingTeams().some(t => (t.clubTeamName ?? '').trim().toLowerCase() === name);
    });

    /** Advisory only — see team-name-hints. Does not feed step1Done. */
    readonly nameIsBareYear = computed(() => isBareYearName(this.teamName()));

    /** Step 1 (Name) complete: team name present, not echoing the club name, not a duplicate. */
    readonly step1Done = computed(() =>
        this.teamName().trim().length > 0 && !this.nameContainsClub() && !this.nameIsDuplicate(),
    );

    /** Step 2 (Details) complete: grad year + LOP both picked. */
    readonly step2Done = computed(() =>
        !!this.gradYear() && !!this.levelOfPlay(),
    );

    /** Which step's frame should pulse / accept input now. Always falls forward. */
    readonly activeStep = computed<1 | 2 | 3>(() =>
        !this.step1Done() ? 1 : !this.step2Done() ? 2 : 3,
    );

    /** Age picker is unlocked once the team identity is fully and validly captured. */
    readonly stage4Ready = computed(() => this.step1Done() && this.step2Done());

    /** Submit gate — every field must be present AND an age group selected. */
    readonly canSubmit = computed(() => this.stage4Ready() && !!this.selectedAgeGroup());

    /** Tooltip naming what's still needed when the submit button is disabled. */
    readonly disabledReason = computed(() => {
        if (!this.teamName().trim()) return 'Enter a team name';
        if (this.nameContainsClub())  return 'Remove the club name from the team name';
        if (this.nameIsDuplicate())   return 'That name is already in your library';
        if (!this.gradYear())         return 'Pick a grad year';
        if (!this.levelOfPlay())      return 'Pick a level of play';
        if (!this.selectedAgeGroup()) return 'Pick an age group';
        return '';
    });

    /** True when the grad year resolves to a recommended (starred) age group. */
    readonly hasRecommended = computed(() =>
        resolveRecommendedAgeGroupId(this.ageGroups(), this.gradYear()) !== '',
    );

    /**
     * The secondary path: library row only, no event registration. The toast is
     * warning-toned and names the event it did NOT register for — this is the exact
     * moment the old flow lost reps, so the message must not be the cheerful
     * "added" it used to be.
     */
    saveLibraryOnly(): void {
        this.submitted.set(true);
        this.errorMsg.set(null);
        if (!this.stage4Ready() || this.saving()) return;

        this.saving.set(true);
        this.savingLibraryOnly.set(true);

        const teamName = this.teamName().trim();
        this.teamReg.createClubTeam({
            clubTeamName: teamName,
            clubTeamGradYear: this.gradYear(),
            levelOfPlay: this.levelOfPlay().trim() || undefined,
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (newTeam) => {
                    this.saving.set(false);
                    this.savingLibraryOnly.set(false);
                    this.toast.show(
                        `${teamName} saved to your library — NOT registered for ${this.eventName()}.`,
                        'warning', 5000);
                    this.savedLibraryOnly.emit(newTeam);
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    this.savingLibraryOnly.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Failed to create team.');
                },
            });
    }

    save(): void {
        this.submitted.set(true);
        this.errorMsg.set(null);

        if (!this.stage4Ready() || this.saving()) return;
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
                                // A rejected registration never lands here: the API answers it with
                                // HTTP 400 (RegisterTeamResponse.success=false in the body), which
                                // HttpClient routes to `error:` below.
                                this.saving.set(false);
                                const msg = regResp.isWaitlisted
                                    ? `${teamName} waitlisted for ${regResp.waitlistAgegroupName ?? ''}`
                                    : `${teamName} registered for the event!`;
                                this.toast.show(msg, regResp.isWaitlisted ? 'warning' : 'success', 3000);
                                this.saved.emit();
                            },
                            error: (err: unknown) => {
                                // Library entry was created but registration was rejected (business
                                // rule, fee gap, or a real fault). Keep the team in the library and
                                // tell the rep WHY, in the server's own words, so they can act on it.
                                this.saving.set(false);
                                this.errorMsg.set(
                                    extractHttpErrorMessage(err, 'The event registration failed.') +
                                    ' Your team is saved in your library; you can register it from there.',
                                );
                            },
                        });
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    this.errorMsg.set(extractHttpErrorMessage(err, 'Failed to create team.'));
                },
            });
    }

}
