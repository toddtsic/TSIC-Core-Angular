import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe } from '@angular/common';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/wizards/team-registration-wizard/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import type { TeamsMetadataResponse, AgeGroupDto, RegisteredTeamDto } from '@core/api';

/**
 * Teams step — register/unregister teams for the event.
 * Shows team list, age groups, and registration status.
 */
@Component({
    selector: 'app-trw-teams-step',
    standalone: true,
    imports: [CurrencyPipe],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Register Teams</h5>
      </div>
      <div class="card-body">
        @if (loading()) {
          <div class="text-center py-4">
            <div class="spinner-border text-primary" role="status">
              <span class="visually-hidden">Loading teams...</span>
            </div>
          </div>
        } @else if (error()) {
          <div class="alert alert-danger">{{ error() }}</div>
        } @else {
          @if (clubName()) {
            <div class="alert alert-info border-0 mb-3">
              <div class="fw-semibold">{{ clubName() }}</div>
              <div class="small text-muted">Select age groups and register your teams below.</div>
            </div>
          }

          <!-- Registered teams summary -->
          @if (registeredTeams().length > 0) {
            <h6 class="fw-semibold mb-2">Registered Teams ({{ registeredTeams().length }})</h6>
            <div class="table-responsive mb-4">
              <table class="table table-sm align-middle">
                <thead class="table-light">
                  <tr>
                    <th>Team</th>
                    <th>Age Group</th>
                    <th class="text-end">Fee</th>
                    <th class="text-end">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  @for (team of registeredTeams(); track team.teamId) {
                    <tr>
                      <td>{{ team.teamName }}</td>
                      <td>{{ team.ageGroupName }}</td>
                      <td class="text-end">{{ team.feeTotal | currency }}</td>
                      <td class="text-end">
                        @if (canUnregister(team)) {
                          <button type="button" class="btn btn-sm btn-outline-danger"
                                  (click)="unregisterTeam(team)">
                            Remove
                          </button>
                        } @else {
                          <span class="badge bg-success">Paid</span>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }

          <!-- Available age groups for new registration -->
          @if (ageGroups().length > 0) {
            <h6 class="fw-semibold mb-2">Register New Team</h6>
            <div class="row g-2">
              @for (ag of ageGroups(); track ag.ageGroupId) {
                <div class="col-12 col-sm-6 col-md-4">
                  <button type="button" class="btn btn-outline-primary w-100 text-start"
                          (click)="registerTeam(ag)">
                    <div class="fw-semibold">{{ ag.ageGroupName }}</div>
                    @if (ag.teamFee) {
                      <div class="small text-muted">{{ ag.teamFee | currency }} per team</div>
                    }
                  </button>
                </div>
              }
            </div>
          }
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamTeamsStepComponent implements OnInit {
    readonly proceedToPayment = output<void>();

    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly clubName = signal<string | null>(null);
    readonly registeredTeams = signal<RegisteredTeamDto[]>([]);
    readonly ageGroups = signal<AgeGroupDto[]>([]);

    ngOnInit(): void {
        this.loadTeamsMetadata();
    }

    private loadTeamsMetadata(): void {
        this.loading.set(true);
        this.error.set(null);

        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (meta: TeamsMetadataResponse) => {
                    this.loading.set(false);
                    this.clubName.set(meta.clubName || null);
                    this.registeredTeams.set(meta.registeredTeams || []);
                    this.ageGroups.set(meta.ageGroups || []);
                    // Propagate metadata to payment service + state
                    this.state.teamPayment.setTeams(meta.registeredTeams || []);
                    this.state.teamPayment.setJobPath(this.state.jobPath());
                    this.state.teamPayment.setPaymentConfig(
                        meta.paymentMethodsAllowedCode,
                        meta.bAddProcessingFees,
                        meta.bApplyProcessingFeesToTeamDeposit,
                    );
                    this.state.setHasActiveDiscountCodes(meta.hasActiveDiscountCodes);
                },
                error: (err: unknown) => {
                    this.loading.set(false);
                    console.error('[TeamsStep] Metadata load failed', err);
                    this.error.set('Failed to load team registration data.');
                },
            });
    }

    registerTeam(ageGroup: AgeGroupDto): void {
        this.teamReg.registerTeamForEvent({
            ageGroupId: ageGroup.ageGroupId,
            teamName: `${this.clubName() || 'Team'} - ${ageGroup.ageGroupName}`,
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.toast.show('Team registered successfully!', 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    console.error('[TeamsStep] Registration failed', err);
                    this.toast.show('Failed to register team.', 'danger', 4000);
                },
            });
    }

    unregisterTeam(team: RegisteredTeamDto): void {
        if (!team.teamId) return;
        this.teamReg.unregisterTeamFromEvent(team.teamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.toast.show('Team removed.', 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    console.error('[TeamsStep] Unregister failed', err);
                    this.toast.show('Failed to remove team.', 'danger', 4000);
                },
            });
    }

    canUnregister(team: RegisteredTeamDto): boolean {
        return team.paidTotal === 0;
    }
}
