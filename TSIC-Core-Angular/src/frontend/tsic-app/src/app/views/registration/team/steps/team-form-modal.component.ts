import { ChangeDetectionStrategy, Component, computed, DestroyRef, Input, inject, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
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
    imports: [FormsModule, TsicDialogComponent],
    template: `
    <tsic-dialog [open]="true" size="sm" (requestClose)="closed.emit()">
      <div class="modal-content form-modal">

        <!-- Hero banner — matches register picker styling -->
        <div class="form-hero">
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

          <!-- Team Name -->
          <div class="form-row">
            <label for="tf-name" class="field-label">Team Name</label>
            <input id="tf-name" type="text" class="field-input"
                   [value]="teamName()" (input)="teamName.set($any($event.target).value)"
                   placeholder="e.g. 2028 Blue"
                   [class.is-required]="!teamName().trim()"
                   [class.is-invalid]="submitted() && (!teamName().trim() || nameContainsClub())" />
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
          </div>

          <!-- Grad Year -->
          <div class="form-row">
            <label for="tf-year" class="field-label">Grad Year</label>
            <select id="tf-year" class="field-select"
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

          <!-- Level of Play — pill selector matching picker modal -->
          <div class="form-row">
            <label class="field-label">Level of Play</label>
            <div class="lop-pills" role="radiogroup" aria-label="Level of play">
              @for (lop of lopChoices; track lop.value) {
                <button type="button" class="lop-pill" role="radio"
                        [class.active]="levelOfPlay() === lop.value"
                        [class.is-invalid]="submitted() && !levelOfPlay()"
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

          @if (errorMsg()) {
            <div class="alert alert-danger rounded-0 border-0 py-2 px-3 mb-0 small">{{ errorMsg() }}</div>
          }
        </div>

        <!-- Footer -->
        <div class="form-footer">
          <button type="button" class="btn btn-sm btn-outline-secondary" (click)="closed.emit()">Cancel</button>
          <button type="button" class="btn btn-sm btn-primary fw-semibold" (click)="save()" [disabled]="saving()">
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
      /* ── Hero Banner — matches picker-hero ── */
      .form-hero {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: linear-gradient(135deg, rgba(var(--bs-primary-rgb), 0.08) 0%, rgba(var(--bs-primary-rgb), 0.02) 100%);
        border-bottom: 2px solid rgba(var(--bs-primary-rgb), 0.12);
      }

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
