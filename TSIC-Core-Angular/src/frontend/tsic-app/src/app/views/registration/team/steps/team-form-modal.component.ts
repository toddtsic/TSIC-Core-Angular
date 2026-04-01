import { ChangeDetectionStrategy, Component, DestroyRef, Input, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import type { AgeGroupDto } from '@core/api';

/**
 * Modal for adding a new ClubTeam to the club library.
 */
@Component({
    selector: 'app-team-form-modal',
    standalone: true,
    imports: [FormsModule, TsicDialogComponent],
    template: `
    <tsic-dialog [open]="true" size="sm" (requestClose)="closed.emit()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title"><i class="bi bi-plus-circle me-2"></i>Add Team to Library</h5>
          <button type="button" class="btn-close" (click)="closed.emit()" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          <div class="row g-2">
            <div class="col-12">
              <label for="tf-name" class="form-label small fw-medium mb-1">Team Name</label>
              <input id="tf-name" type="text" class="form-control form-control-sm"
                     [value]="teamName()" (input)="teamName.set($any($event.target).value)"
                     placeholder="e.g. 2028 Blue"
                     [class.is-required]="!teamName().trim()"
                     [class.is-invalid]="submitted() && !teamName().trim()" />
              @if (submitted() && !teamName().trim()) {
                <div class="invalid-feedback">Required</div>
              }
            </div>
            <div class="col-6">
              <label for="tf-year" class="form-label small fw-medium mb-1">Grad Year</label>
              <select id="tf-year" class="form-select form-select-sm"
                      [ngModel]="gradYear()" (ngModelChange)="gradYear.set($event)"
                      [class.is-required]="!gradYear()"
                      [class.is-invalid]="submitted() && !gradYear()">
                <option value="">Select</option>
                @for (ag of ageGroups; track ag.ageGroupId) {
                  <option [value]="ag.ageGroupName">{{ ag.ageGroupName }}</option>
                }
              </select>
              @if (submitted() && !gradYear()) {
                <div class="invalid-feedback">Required</div>
              }
            </div>
            <div class="col-6">
              <label for="tf-lop" class="form-label small fw-medium mb-1">
                Level of Play <span class="text-muted fw-normal">(optional)</span>
              </label>
              <input id="tf-lop" type="text" class="form-control form-control-sm"
                     [value]="levelOfPlay()" (input)="levelOfPlay.set($any($event.target).value)"
                     placeholder="e.g. 1, 2, 3" />
            </div>
          </div>

          @if (errorMsg()) {
            <div class="alert alert-danger py-2 small mt-2 mb-0">{{ errorMsg() }}</div>
          }
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-sm btn-outline-secondary" (click)="closed.emit()">Cancel</button>
          <button type="button" class="btn btn-sm btn-primary fw-semibold" (click)="save()" [disabled]="saving()">
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
    @Input() ageGroups: AgeGroupDto[] = [];

    readonly saved = output<void>();
    readonly closed = output<void>();

    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly teamName = signal('');
    readonly gradYear = signal('');
    readonly levelOfPlay = signal('');
    readonly submitted = signal(false);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);

    save(): void {
        this.submitted.set(true);
        if (!this.teamName().trim() || !this.gradYear()) return;

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
