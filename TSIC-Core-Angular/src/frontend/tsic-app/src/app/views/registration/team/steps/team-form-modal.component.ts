import { ChangeDetectionStrategy, Component, computed, DestroyRef, Input, inject, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { DragDropModule } from '@angular/cdk/drag-drop';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import type { ClubTeamDto } from '@core/api';

/**
 * Modal for adding a new ClubTeam to the club library, or editing an existing one
 * (when `editingTeam` is supplied). Edit mode is only ever opened for teams whose
 * `bHasBeenScheduled` is false — the library UI enforces that upstream.
 */
@Component({
    selector: 'app-team-form-modal',
    standalone: true,
    imports: [FormsModule, TsicDialogComponent, DragDropModule],
    template: `
    <tsic-dialog [open]="true" size="sm" (requestClose)="closed.emit()">
      <div class="modal-content form-modal"
           cdkDrag
           [cdkDragRootElement]="'.tsic-dialog'"
           cdkDragBoundary="body">

        <!-- Hero banner — matches register picker styling. Doubles as the
             drag handle so the rep can shift the modal to peek at the
             library table beneath. -->
        <div class="form-hero" cdkDragHandle>
          <h5 class="form-hero-title mb-0">
            @if (isEdit()) {
              <i class="bi bi-pencil-square me-1"></i>Edit Library Team
            } @else {
              <i class="bi bi-plus-circle me-1"></i>Add Team to Library
            }
          </h5>
          <button type="button" class="form-hero-close" (click)="closed.emit()" aria-label="Close">
            <i class="bi bi-x-lg"></i>
          </button>
        </div>

        <!-- Form body -->
        <div class="form-body">

          <!-- ── Step 1 — Name your team ─────────────────────────── -->
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
            <div class="wizard-tip">
              Instead of entering <span class="text-danger fw-semibold">{{ clubName }} 2028 Blue</span>,
              enter <span class="text-success fw-semibold">2028 Blue</span> — schedules already display your club name.
            </div>
            @if (submitted() && !teamName().trim()) {
              <div class="field-error">Required</div>
            }
            @if (submitted() && teamName().trim() && nameContainsClub()) {
              <div class="field-error">Remove your club name from the team name.</div>
            }
            @if (!submitted() && nameContainsClub()) {
              <div class="field-error" style="color: var(--bs-warning)">
                <i class="bi bi-exclamation-triangle me-1"></i>Contains your club name — please remove it.
              </div>
            }
            @if (nameIsDuplicate()) {
              <div class="field-error">
                <i class="bi bi-exclamation-triangle me-1"></i>
                <strong>{{ teamName().trim() }}</strong> is already in your library — pick a different name.
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
              <label for="tf-year" class="field-label">Players' Grad Year</label>
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
              <div class="lop-pills" role="radiogroup" aria-label="Level of play">
                @for (lop of lopChoices; track lop.value) {
                  <button type="button" class="lop-pill" role="radio"
                          [class.active]="levelOfPlay() === lop.value"
                          [class.is-invalid]="submitted() && !levelOfPlay()"
                          [disabled]="!step1Done()"
                          [attr.aria-checked]="levelOfPlay() === lop.value"
                          (click)="levelOfPlay.set(lop.value)">
                    {{ lop.label }}
                  </button>
                }
              </div>
              <div class="wizard-tip">Overall team assessment — rep can adjust per tournament by editing the team.</div>
              @if (submitted() && !levelOfPlay()) {
                <div class="field-error">Required</div>
              }
            </div>
          </div>

          @if (errorMsg()) {
            <div class="alert alert-danger rounded-0 border-0 py-2 px-3 mb-0 small">{{ errorMsg() }}</div>
          }
        </div>

        <!-- Footer -->
        <div class="form-footer">
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

      /* ── LOP Pills (copy of picker's .lop-pill) ── */
      .lop-pills {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
      }

      .lop-pill {
        flex: 1 1 0;
        min-width: 44px;
        padding: var(--space-1) var(--space-2);
        border: 1.5px solid var(--border-color);
        border-radius: var(--radius-full);
        background: var(--brand-surface);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
        cursor: pointer;
        transition: all 0.12s ease;
        text-align: center;

        &:hover { border-color: var(--bs-primary); }

        &.active {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.1);
          color: var(--bs-primary);
          font-weight: var(--font-weight-semibold);
        }

        &.is-invalid:not(.active) {
          border-color: rgba(var(--bs-danger-rgb), 0.5);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
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
    @Input() clubName = '';
    /** When supplied, the modal is in edit mode and updates this team instead of creating. */
    @Input() editingTeam: ClubTeamDto | null = null;
    /** Existing library teams — used to block duplicate names (case-insensitive). */
    @Input() existingTeams: readonly ClubTeamDto[] = [];

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

    /** Level-of-play pill choices. Endpoints get friendly labels; middle values are terse. */
    readonly lopChoices: ReadonlyArray<{ value: string; label: string }> = [
        { value: '1', label: '1 (weakest)' },
        { value: '2', label: '2' },
        { value: '3', label: '3' },
        { value: '4', label: '4' },
        { value: '5', label: '5 (strongest)' },
    ];

    readonly teamName = signal('');
    readonly gradYear = signal('');
    readonly levelOfPlay = signal('');
    readonly submitted = signal(false);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);

    readonly isEdit = computed(() => this.editingTeam != null);

    /** True when the team name contains the club name (case-insensitive). */
    readonly nameContainsClub = computed(() => {
        const club = this.clubName.trim().toLowerCase();
        const name = this.teamName().trim().toLowerCase();
        return club.length > 0 && name.length > 0 && name.includes(club);
    });

    /** True when the team name matches an existing library team (case-insensitive),
     *  excluding the team being edited. */
    readonly nameIsDuplicate = computed(() => {
        const name = this.teamName().trim().toLowerCase();
        if (!name) return false;
        const editingId = this.editingTeam?.clubTeamId;
        return this.existingTeams.some(t =>
            t.clubTeamId !== editingId &&
            (t.clubTeamName ?? '').trim().toLowerCase() === name,
        );
    });

    /** Step 1 (Name) complete: team name present, not echoing the club name,
     *  and not duplicating an existing library team. */
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
                        this.toast.show('Library team updated.', 'success', 2000);
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
