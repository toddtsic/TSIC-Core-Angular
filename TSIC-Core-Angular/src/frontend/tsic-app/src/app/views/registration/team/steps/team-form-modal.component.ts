import { ChangeDetectionStrategy, Component, computed, DestroyRef, Input, inject, OnInit, output, signal, input } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import type { ClubTeamDto } from '@core/api';
import { LevelOfPlayPickerComponent } from '@shared/teams/level-of-play-picker.component';
import { clubNameInTeamName, isBareYearName } from '@shared/teams/team-name-hints';
import { TeamNameSchedulePreviewComponent } from '@shared/teams/team-name-schedule-preview.component';

/**
 * Modal for editing an existing library team (when `editingTeam` is supplied), or
 * adding one to the library WITHOUT registering it. Edit mode is only ever opened
 * when shared/teams/club-team-locks.ts allows it — the library UI enforces that
 * upstream.
 *
 * Add mode is now the exception, not the rule: while registration is open the
 * wizard sends every add through add-and-register-team-modal, because a plain
 * library add left reps believing the team was in the event. This form's add mode
 * remains for the case where there is nothing to register INTO — registration
 * closed for this event — so "added to library" is the whole truth.
 */
@Component({
    selector: 'app-team-form-modal',
    standalone: true,
    imports: [FormsModule, TsicDialogComponent, LevelOfPlayPickerComponent, TeamNameSchedulePreviewComponent],
    template: `
    <tsic-dialog [open]="true" size="sm" (requestClose)="dismiss()">
      <div class="modal-content form-modal">

        <!-- Hero banner — matches register picker styling. Doubles as the
             drag handle (tsic-dialog resolves the first child of .modal-content
             as its handle) so the rep can shift the modal to peek at the
             library table beneath. -->
        <div class="form-hero">
          <h5 class="form-hero-title mb-0">
            @if (isEdit()) {
              <i class="bi bi-pencil-square me-1"></i>Edit Library Team
            } @else {
              <i class="bi bi-plus-circle me-1"></i>Add Team to Library
            }
          </h5>
          <button type="button" class="form-hero-close" (click)="dismiss()" aria-label="Close">
            <i class="bi bi-x-lg"></i>
          </button>
        </div>

        <!-- Form body -->
        <div class="form-body">
          @if (phase() === 'added') {
            <!-- "Add as a new team instead" landed. The old row is still active; offer the archive
                 that keeps its history where it belongs. Locked = registered for this event. -->
            <div class="library-aside library-aside--lead" role="status">
              <i class="bi bi-check-circle" aria-hidden="true"></i>
              <span><strong>{{ addedName() }} is in your library.</strong>
                @if (archiveLockReason(); as lock) {
                  {{ editingTeam?.clubTeamName }} stays in your active library: {{ lock }}.
                } @else {
                  Archive <strong>{{ editingTeam?.clubTeamName }}</strong>? Its event history stays with it,
                  it leaves the list you register from, and you can restore it any time.
                }
              </span>
            </div>
            @if (errorMsg()) {
              <div class="alert alert-danger rounded-0 border-0 py-2 px-3 mb-0 small">{{ errorMsg() }}</div>
            }
          } @else {

          <!-- Edit mode: shout the distinction before anything else (Todd 2026-09-24). A library
               edit never reaches an event; the event copy is changed in Team Registration. -->
          @if (isEdit()) {
            <div class="library-aside library-aside--lead" role="note">
              <i class="bi bi-collection" aria-hidden="true"></i>
              <span><strong>This changes your library only.</strong> Teams already registered for an event keep
                the name, grad year and level of play they were registered with.
                @if (eventName()) {
                  <strong>To change a team for {{ eventName() }}, return to your registration: open your menu at the
                    top right and choose Team Registration.</strong>
                } @else {
                  <strong>To change a team for this event, use the Registered Teams list in your registration.</strong>
                }</span>
            </div>
          }

          <!-- ── Step 1 — Name your team ─────────────────────────────────────── -->
          <div class="step-section"
               role="group" aria-labelledby="tf-step-1-title"
               [class.is-active]="activeStep() === 1"
               [class.is-completed]="step1Done()">
            <div class="step-eyebrow">
              <span class="step-circle"
                    [class.is-active]="activeStep() === 1"
                    [class.is-completed]="step1Done()">
                @if (step1Done()) { <i class="bi bi-check-lg"></i> } @else { 1 }
              </span>
              <span class="step-title" id="tf-step-1-title">Name your team</span>
            </div>

            <input id="tf-name" type="text" class="field-input"
                   [value]="teamName()" (input)="teamName.set($any($event.target).value)"
                   placeholder="e.g. 2028 Blue"
                   [class.is-required]="!teamName().trim()"
                   [class.is-invalid]="submitted() && (!teamName().trim() || nameContainsClub() || nameIsDuplicate())"
                   [class.has-warning]="!submitted() && (nameContainsClub() || nameIsDuplicate())" />
            <!-- The club-name hammer: the live schedule label, red when the club name is in it. -->
            <team-name-schedule-preview [clubName]="clubName()" [teamName]="teamName()" />
            @if (submitted() && !teamName().trim()) {
              <div class="field-error">Required</div>
            }
            @if (nameIsDuplicate()) {
              <div class="field-error">
                <i class="bi bi-exclamation-triangle me-1"></i>
                <strong>{{ teamName().trim() }}</strong> is already in your library — pick a different name.
              </div>
            }
            <!-- Soft nudge, never a block — same copy as add-and-register-team-modal. -->
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
               role="group" aria-labelledby="tf-step-2-title"
               [class.is-active]="activeStep() === 2"
               [class.is-completed]="step2Done()"
               [class.is-locked]="!step1Done()">
            <div class="step-eyebrow">
              <span class="step-circle"
                    [class.is-active]="activeStep() === 2"
                    [class.is-completed]="step2Done()">
                @if (step2Done()) { <i class="bi bi-check-lg"></i> } @else { 2 }
              </span>
              <span class="step-title" id="tf-step-2-title">Team details</span>
            </div>

            <div class="form-row">
              <label for="tf-year" class="field-label">Grad Year</label>
              <select id="tf-year" class="field-select"
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

            <div class="form-row">
              <label class="field-label">Level of Play</label>
              <app-level-of-play-picker
                [fill]="true"
                labels="full"
                [disabled]="!step1Done()"
                [invalid]="submitted() && !levelOfPlay()"
                [selected]="levelOfPlay()"
                (selectedChange)="levelOfPlay.set($event)" />
              <div class="wizard-tip">Overall team assessment for your library. Each event registration keeps its own level of play, set when you register.</div>
              @if (submitted() && !levelOfPlay()) {
                <div class="field-error">Required</div>
              }
            </div>
          </div>

          @if (errorMsg()) {
            <div class="alert alert-danger rounded-0 border-0 py-2 px-3 mb-0 small">{{ errorMsg() }}</div>
          }

          <!-- Add mode on the standalone library page: say what this save is and is not.
               The wizard's plain-add path lost reps on exactly this point. -->
          @if (!isEdit() && eventName()) {
            <div class="library-aside">
              <i class="bi bi-collection" aria-hidden="true"></i>
              <span>Saved to your library for future events &mdash; <strong>not registered for {{ eventName() }}</strong>.
                <strong>To enter it, return to your registration: open your menu at the top right and choose Team Registration.</strong></span>
            </div>
          }

          <!-- The one-team-through-time nudge (Todd 2026-09-24). The gate is gone; this is a hint.
               Fires only on a row with event history when the GRAD YEAR moved or the year in the
               NAME moved — the two edits that almost always mean "a different squad", whose
               history should not be inherited. A plain rebrand (Blue → Navy) never sees it.
               Save stays live: a typo'd grad year on a team with history is a real fix. -->
          @if (looksLikeDifferentTeam()) {
            <div class="library-aside team-nudge" role="note">
              <i class="bi bi-signpost-split" aria-hidden="true"></i>
              <span>
                <strong>Looks like a different team?</strong> {{ editingTeam?.clubTeamName }} has been registered
                for events. If that squad has moved on, keep its history: add
                <strong>{{ teamName().trim() }}</strong> as a new team and archive {{ editingTeam?.clubTeamName }}.
                <span class="team-nudge-actions">
                  <button type="button" class="btn btn-sm btn-primary fw-semibold"
                          [disabled]="saving() || !canSubmit()" (click)="addInstead()">
                    <i class="bi bi-plus-circle me-1"></i>Add {{ teamName().trim() }} as a new team
                  </button>
                  <button type="button" class="btn btn-sm btn-outline-secondary" (click)="nudgeDismissed.set(true)">
                    No, I'm fixing {{ editingTeam?.clubTeamName }}
                  </button>
                </span>
              </span>
            </div>
          }
          }
        </div>

        <!-- Footer -->
        <div class="form-footer">
          @if (phase() === 'added') {
            @if (archiveLockReason()) {
              <button type="button" class="btn btn-sm btn-primary fw-semibold" (click)="keepBoth()">Done</button>
            } @else {
              <button type="button" class="btn btn-sm btn-outline-secondary" [disabled]="saving()" (click)="keepBoth()">Keep both</button>
              <button type="button" class="btn btn-sm btn-primary fw-semibold" [disabled]="saving()" (click)="archiveOld()">
                @if (saving()) {
                  <span class="spinner-border spinner-border-sm me-1"></span>Archiving...
                } @else {
                  <i class="bi bi-box-arrow-in-down me-1"></i>Archive {{ editingTeam?.clubTeamName }}
                }
              </button>
            }
          } @else {
          <button type="button" class="btn btn-sm btn-outline-secondary" (click)="closed.emit()">Cancel</button>
          <button type="button" class="btn btn-sm btn-primary fw-semibold"
                  (click)="save()" [disabled]="saving() || !canSubmit()">
            @if (saving()) {
              <span class="spinner-border spinner-border-sm me-1"></span>{{ isEdit() ? 'Saving...' : 'Adding...' }}
            } @else if (isEdit()) {
              <i class="bi bi-check-lg me-1"></i>Save Changes
            } @else {
              <i class="bi bi-plus-circle me-1"></i>Add to Library
            }
          </button>
          }
        </div>
      </div>
    </tsic-dialog>
  `,
    styles: [`
      /* ── Hero Banner — matches picker-hero. Doubles as the cdkDragHandle
         for the modal; cursor + user-select tweaks signal grab-ability. ── */
      .form-hero {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: linear-gradient(135deg, rgba(var(--bs-primary-rgb), 0.08) 0%, rgba(var(--bs-primary-rgb), 0.02) 100%);
        border-bottom: 2px solid rgba(var(--bs-primary-rgb), 0.12);
        cursor: grab;
        user-select: none;

        &:active { cursor: grabbing; }
      }

      /* Close button keeps its own cursor — drag handle shouldn't override
         interactive children. CDK drag already excludes button targets, but
         the cursor needs an explicit override. */
      .form-hero-close { cursor: pointer; }

      .form-hero-title {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);

        i { color: var(--bs-primary); }
      }

      .form-hero-close {
        border: none;
        background: transparent;
        color: var(--brand-text-muted);
        padding: var(--space-1) var(--space-2);
        line-height: 1;
        border-radius: var(--radius-sm);
        cursor: pointer;
        transition: background-color 0.15s ease, color 0.15s ease;
      }

      .form-hero-close:hover { color: var(--brand-text); background: rgba(var(--bs-body-color-rgb), 0.05); }
      .form-hero-close:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

      /* ── Body ── */
      .form-body { padding: var(--space-2) var(--space-3) var(--space-1); }

      .form-row + .form-row { margin-top: var(--space-2); }

      /* Grad-year disambiguation — copy + styling kept in sync with the
         identical block in add-and-register-team-modal. */
      .grad-year-tip {
        margin-top: var(--space-1);
        font-size: var(--font-size-xs);
        line-height: var(--line-height-normal);
        color: var(--brand-text-muted);
      }
      .grad-year-tip strong { color: var(--brand-text); }
      .grad-year-tip em { color: var(--bs-danger); font-style: normal; font-weight: var(--font-weight-semibold); }

      /* LOP pills render via <app-level-of-play-picker [fill]="true" labels="full">,
         which owns its own styles. */

      /* Bare-year nudge — advisory tone (kept in sync with add-and-register-team-modal). */
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

      .library-aside {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        margin-top: var(--space-3);
        padding: var(--space-2) var(--space-3);
        background: var(--bs-warning-bg-subtle);
        border: 1px solid var(--bs-warning);
        border-left-width: 4px;
        border-radius: var(--radius-md);
        font-size: var(--font-size-xs);
        line-height: var(--line-height-normal);
        color: var(--bs-warning-text-emphasis);

        i { flex-shrink: 0; margin-top: 1px; font-size: var(--font-size-base); }
        strong { font-weight: var(--font-weight-bold); }

        // Edit mode's lead-in: first thing in the body, informational rather than a warning.
        &--lead {
          margin-top: 0;
          margin-bottom: var(--space-3);
          background: color-mix(in srgb, var(--bs-primary) 8%, var(--bs-body-bg));
          border-color: var(--bs-primary);
          color: var(--brand-text);
          font-size: var(--font-size-sm);
        }
      }

      // The different-team nudge: a signpost, not a warning. Declared AFTER the base aside so
      // it wins the cascade at equal specificity.
      .library-aside.team-nudge {
        background: color-mix(in srgb, var(--bs-info) 10%, var(--bs-body-bg));
        border-color: var(--bs-info);
        color: var(--brand-text);
      }
      .team-nudge-actions {
        display: flex; flex-wrap: wrap; gap: var(--space-2);
        margin-top: var(--space-2);
      }

      /* ── Footer ── */
      .form-footer {
        display: flex;
        justify-content: flex-end;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border-top: 1px solid var(--border-color);
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamFormModalComponent implements OnInit {
    readonly clubName = input('');
    /** The event the rep is signed in to, set by the library page only. Add mode: the aside says this
     *  save does NOT enter the team for it. Edit mode: the callout says where the event copy is changed
     *  ("return to your registration"). The wizard leaves it empty — the rep is already there. */
    readonly eventName = input('');
    /** When supplied, the modal is in edit mode and updates this team instead of creating. */
    // TODO: Skipped for migration because:
    //  Your application code writes to the input. This prevents migration.
    @Input() editingTeam: ClubTeamDto | null = null;
    /** Existing library teams — used to block duplicate names (case-insensitive). */
    readonly existingTeams = input<readonly ClubTeamDto[]>([]);
    /**
     * Edit mode: why the team being edited cannot be archived right now (null = it can). The
     * parent computes it from shared/teams/club-team-locks.ts; the modal only needs it for the
     * "add as a new team instead" follow-up, which offers to archive the old row.
     */
    readonly archiveLockReason = input<string | null>(null);

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

    readonly teamName = signal('');
    readonly gradYear = signal('');
    readonly levelOfPlay = signal('');
    readonly submitted = signal(false);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);

    readonly isEdit = computed(() => this.editingTeam != null);

    /** The whole club name is in the team name — blocks the save; the preview says why. */
    readonly nameContainsClub = computed(() => clubNameInTeamName(this.clubName(), this.teamName()) === 'full');

    /** True when the team name matches an existing library team (case-insensitive),
     *  excluding the team being edited. */
    readonly nameIsDuplicate = computed(() => {
        const name = this.teamName().trim().toLowerCase();
        if (!name) return false;
        const editingId = this.editingTeam?.clubTeamId;
        return this.existingTeams().some(t =>
            t.clubTeamId !== editingId &&
            (t.clubTeamName ?? '').trim().toLowerCase() === name,
        );
    });

    /** Advisory only — see team-name-hints. Does not feed step1Done. */
    readonly nameIsBareYear = computed(() => isBareYearName(this.teamName()));

    /** Step 1 (Name) complete: team name present, not echoing the club name,
     *  and not duplicating an existing library team (the row being edited excluded). */
    readonly step1Done = computed(() =>
        this.teamName().trim().length > 0
        && !this.nameContainsClub()
        && !this.nameIsDuplicate(),
    );

    /** Step 2 (Details) complete: grad year + LOP both picked. */
    readonly step2Done = computed(() =>
        !!this.gradYear() && !!this.levelOfPlay(),
    );

    /** Which step's frame should pulse / accept input now. Falls forward in
     *  create mode. In edit mode there's no "active step" — both sections
     *  open as completed so the user isn't pulsed at unprovoked. */
    readonly activeStep = computed<0 | 1 | 2>(() =>
        this.isEdit() ? 0 : (!this.step1Done() ? 1 : 2),
    );

    /** Save gate — every field must be valid; mirrors the submit-time check
     *  so the button can't be clicked into a silent rejection. */
    readonly canSubmit = computed(() => this.step1Done() && this.step2Done());

    // ── "Looks like a different team?" ─────────────────────────────────
    /** 'added' after "add as a new team instead" succeeded: the body becomes the archive offer. */
    readonly phase = signal<'form' | 'added'>('form');
    readonly addedName = signal('');
    readonly nudgeDismissed = signal(false);

    /**
     * The two edits that almost always mean a different squad, on a row whose history would then
     * be inherited: the grad year moved, or the four-digit year in the name moved. Either alone
     * fires it (reps forget the grad year field). A name with no year, or a year appearing where
     * there was none, is a rename, not a new team. Never in add mode, never without history.
     */
    readonly looksLikeDifferentTeam = computed(() => {
        const t = this.editingTeam;
        if (!t || !t.bHasEventRegistrations || this.nudgeDismissed() || this.phase() !== 'form') return false;
        const gradMoved = !!t.clubTeamGradYear && !!this.gradYear() && t.clubTeamGradYear !== this.gradYear();
        const was = TeamFormModalComponent.yearToken(t.clubTeamName);
        const now = TeamFormModalComponent.yearToken(this.teamName());
        const nameYearMoved = !!was && !!now && was !== now;
        return gradMoved || nameYearMoved;
    });

    private static yearToken(name: string): string | null {
        return /\b\d{4}\b/.exec(name)?.[0] ?? null;
    }

    /** Create the typed team as a NEW library row and leave the old one untouched, then offer to archive it. */
    addInstead(): void {
        const old = this.editingTeam;
        if (!old) return;
        this.submitted.set(true);
        if (!this.canSubmit() || this.nameContainsClub()) return;
        const name = this.teamName().trim();
        if (name.toLowerCase() === old.clubTeamName.trim().toLowerCase()) {
            this.errorMsg.set(`Give the new team its own name — ${old.clubTeamName} keeps this one.`);
            return;
        }
        if (this.existingTeams().some(e => e.clubTeamName.trim().toLowerCase() === name.toLowerCase())) {
            this.errorMsg.set(`${name} is already in your library.`);
            return;
        }
        this.saving.set(true);
        this.errorMsg.set(null);
        this.teamReg.createClubTeam({
            clubTeamName: name,
            clubTeamGradYear: this.gradYear(),
            levelOfPlay: this.levelOfPlay().trim() || undefined,
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.addedName.set(name);
                    this.phase.set('added');
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Failed to add the team.');
                },
            });
    }

    archiveOld(): void {
        const old = this.editingTeam;
        if (!old || this.archiveLockReason()) return;
        this.saving.set(true);
        this.errorMsg.set(null);
        this.teamReg.archiveClubTeam(old.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.toast.show(`${this.addedName()} added. ${old.clubTeamName} archived with its history.`, 'success', 3500);
                    this.saved.emit();
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Failed to archive the team.');
                },
            });
    }

    keepBoth(): void {
        this.toast.show(`${this.addedName()} added to your library.`, 'success', 2500);
        this.saved.emit();
    }

    /** X / backdrop: once a team has been added the list has changed, so the parent must reload. */
    dismiss(): void {
        if (this.phase() === 'added') this.saved.emit();
        else this.closed.emit();
    }

    ngOnInit(): void {
        if (this.editingTeam) {
            this.teamName.set(this.editingTeam.clubTeamName);
            this.gradYear.set(this.editingTeam.clubTeamGradYear);
            this.levelOfPlay.set(this.editingTeam.clubTeamLevelOfPlay);
        }
    }

    save(): void {
        this.submitted.set(true);
        if (!this.teamName().trim() || !this.gradYear() || !this.levelOfPlay()) return;
        if (this.nameContainsClub()) return;
        if (this.nameIsDuplicate()) return;

        this.saving.set(true);
        this.errorMsg.set(null);

        const editing = this.editingTeam;
        if (editing) {
            this.teamReg.updateClubTeam(editing.clubTeamId, {
                clubTeamName: this.teamName().trim(),
                clubTeamGradYear: this.gradYear(),
                clubTeamLevelOfPlay: this.levelOfPlay().trim(),
            })
                .pipe(takeUntilDestroyed(this.destroyRef))
                .subscribe({
                    next: () => {
                        this.saving.set(false);
                        this.toast.show('Library team updated. No event was changed.', 'success', 2500);
                        this.saved.emit();
                    },
                    error: (err: unknown) => {
                        this.saving.set(false);
                        const httpErr = err as { error?: { message?: string } };
                        this.errorMsg.set(httpErr?.error?.message || 'Failed to update team.');
                    },
                });
            return;
        }

        this.teamReg.createClubTeam({
            clubTeamName: this.teamName().trim(),
            clubTeamGradYear: this.gradYear(),
            levelOfPlay: this.levelOfPlay().trim() || undefined,
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.toast.show('Team added to library!', 'success', 2000);
                    this.saved.emit();
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Failed to create team.');
                },
            });
    }
}
