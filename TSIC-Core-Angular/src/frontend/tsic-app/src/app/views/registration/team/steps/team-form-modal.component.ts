import { ChangeDetectionStrategy, Component, computed, DestroyRef, Input, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';

/**
 * Modal for adding a new ClubTeam to the club library.
 */
@Component({
    selector: 'app-team-form-modal',
    standalone: true,
    imports: [FormsModule, TsicDialogComponent],
    template: `
    <tsic-dialog [open]="true" size="md" (requestClose)="closed.emit()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title"><i class="bi bi-plus-circle me-2"></i>Add Team to <span class="club-accent">{{ clubName }}</span>'s Library</h5>
          <button type="button" class="btn-close" (click)="closed.emit()" aria-label="Close"></button>
        </div>
        <div class="modal-body p-0">
          <div class="bg-surface-alt">
            <!-- Team Name -->
            <div class="px-4 pt-3 pb-3">
              <label for="tf-name" class="form-label fw-medium mb-1">Team Name <span class="text-danger">*</span></label>
              <div class="form-text mt-0 mb-2">
                Do <strong>NOT</strong> include your club name — schedules already display it.<br>
                <span class="text-danger"><i class="bi bi-x-lg me-1"></i><s>{{ clubName }} 2028 Blue</s></span>
                <span class="ms-3 text-success"><i class="bi bi-check-lg me-1"></i>2028 Blue</span>
              </div>
              <input id="tf-name" type="text" class="form-control"
                     [value]="teamName()" (input)="teamName.set($any($event.target).value)"
                     placeholder="e.g. 2028 Blue"
                     [class.is-required]="!teamName().trim()"
                     [class.is-invalid]="submitted() && (!teamName().trim() || nameContainsClub())" />
              @if (submitted() && !teamName().trim()) {
                <div class="invalid-feedback">Required</div>
              }
              @if (submitted() && teamName().trim() && nameContainsClub()) {
                <div class="invalid-feedback d-block">Remove your club name from the team name.</div>
              }
              @if (!submitted() && nameContainsClub()) {
                <div class="form-text text-warning mt-1">
                  <i class="bi bi-exclamation-triangle me-1"></i>Contains your club name — please remove it.
                </div>
              }
            </div>

            <!-- Grad Year + Level of Play -->
            <div class="row g-0 border-top" style="border-color: var(--border-color) !important">
              <div class="col-6 px-4 py-3 border-end" style="border-color: var(--border-color) !important">
                <label for="tf-year" class="form-label fw-medium mb-1">Grad Year <span class="text-danger">*</span></label>
                <div class="form-text mt-0 mb-1">Majority of team players</div>
                <select id="tf-year" class="form-select"
                        [ngModel]="gradYear()" (ngModelChange)="gradYear.set($event)"
                        [class.is-required]="!gradYear()"
                        [class.is-invalid]="submitted() && !gradYear()">
                  <option value="">Select</option>
                  @for (yr of gradYearOptions; track yr) {
                    <option [value]="yr">{{ yr === 'Adult' ? 'Adult Team' : yr }}</option>
                  }
                </select>
                @if (submitted() && !gradYear()) {
                  <div class="invalid-feedback">Required</div>
                }
              </div>
              <div class="col-6 px-4 py-3">
                <label for="tf-lop" class="form-label fw-medium mb-1">Level of Play <span class="text-danger">*</span></label>
                <div class="form-text mt-0 mb-1">&nbsp;</div>
                <select id="tf-lop" class="form-select"
                        [ngModel]="levelOfPlay()" (ngModelChange)="levelOfPlay.set($event)"
                        [class.is-required]="!levelOfPlay()"
                        [class.is-invalid]="submitted() && !levelOfPlay()">
                  <option value="">Select</option>
                  <option value="1">1 (lowest)</option>
                  <option value="2">2</option>
                  <option value="3">3</option>
                  <option value="4">4</option>
                  <option value="5">5 (highest)</option>
                </select>
                @if (submitted() && !levelOfPlay()) {
                  <div class="invalid-feedback">Required</div>
                }
              </div>
            </div>
          </div>

          @if (errorMsg()) {
            <div class="alert alert-danger rounded-0 border-start-0 border-end-0 border-bottom-0 py-2 px-4 mb-0 small">{{ errorMsg() }}</div>
          }
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-outline-secondary" (click)="closed.emit()">Cancel</button>
          <button type="button" class="btn btn-primary fw-semibold" (click)="save()" [disabled]="saving()">
            @if (saving()) {
              <span class="spinner-border spinner-border-sm me-1"></span>Adding...
            } @else {
              <i class="bi bi-plus-circle me-1"></i>Add Team
            }
          </button>
        </div>
      </div>
    </tsic-dialog>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamFormModalComponent {
    @Input() clubName = '';

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

    /** True when the team name contains the club name (case-insensitive). */
    readonly nameContainsClub = computed(() => {
        const club = this.clubName.trim().toLowerCase();
        const name = this.teamName().trim().toLowerCase();
        return club.length > 0 && name.length > 0 && name.includes(club);
    });

    save(): void {
        this.submitted.set(true);
        if (!this.teamName().trim() || !this.gradYear() || !this.levelOfPlay()) return;
        if (this.nameContainsClub()) return;

        this.saving.set(true);
        this.errorMsg.set(null);

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
