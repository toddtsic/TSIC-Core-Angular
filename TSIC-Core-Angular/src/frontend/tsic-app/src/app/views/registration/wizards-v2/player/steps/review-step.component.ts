import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { PaymentV2Service } from '../state/payment-v2.service';
import { TeamService } from '@views/registration/wizards/player-registration-wizard/team.service';
import { JobService } from '@infrastructure/services/job.service';

/**
 * Review step — summary table of players, teams, and amounts.
 * Waiver summary and server validation errors display.
 */
@Component({
    selector: 'app-prw-review-step',
    standalone: true,
    imports: [CurrencyPipe, DatePipe],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Review &amp; Submit</h5>
      </div>
      <div class="card-body">
        <div class="mb-4">
          <p class="lead mb-1 fw-semibold">You're almost done!</p>
          <p class="text-muted mb-0">Please review the details below before proceeding to payment.</p>
        </div>

        <!-- Server validation errors -->
        @if (state.jobCtx.hasServerValidationErrors()) {
          <div class="alert alert-danger mb-3">
            <div class="fw-semibold mb-1">Validation Errors</div>
            <ul class="mb-0">
              @for (err of state.jobCtx.getServerValidationErrors(); track err.field) {
                <li>{{ err.message || err.field }}</li>
              }
            </ul>
          </div>
        }

        <!-- Players & Teams table -->
        <section class="mb-4">
          <h6 class="fw-semibold mb-2">Players &amp; Teams</h6>
          <div class="table-responsive">
            <table class="table table-sm align-middle mb-0">
              <thead class="table-light">
                <tr>
                  <th scope="col">Player</th>
                  <th scope="col">Team{{ isMultiTeamMode() ? 's' : '' }}</th>
                  <th scope="col" class="text-end">Amount</th>
                </tr>
              </thead>
              <tbody>
                @for (player of selectedPlayers(); track player.userId) {
                  <tr>
                    <td>
                      <div class="fw-semibold">{{ player.name }}</div>
                      @if (player.dob || player.gender) {
                        <div class="small text-muted">
                          @if (player.gender) { <span>{{ player.gender }}</span> }
                          @if (player.gender && player.dob) { <span> &middot; </span> }
                          @if (player.dob) { <span>DOB: {{ player.dob | date:'mediumDate' }}</span> }
                        </div>
                      }
                    </td>
                    <td>
                      @if (getTeamsForPlayer(player.userId).length === 0) {
                        <span class="text-muted">No team selected</span>
                      } @else {
                        <span class="d-inline-flex flex-wrap gap-1">
                          @for (t of getTeamsForPlayer(player.userId); track t) {
                            <span class="badge bg-primary-subtle text-primary-emphasis border border-primary-subtle">{{ t }}</span>
                          }
                        </span>
                      }
                    </td>
                    <td class="text-end">
                      @if (getAmountForPlayer(player.userId) !== null) {
                        {{ getAmountForPlayer(player.userId) | currency }}
                      } @else {
                        <span class="text-muted">&ndash;</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
              @if (paySvc.totalAmount() > 0) {
                <tfoot>
                  <tr>
                    <th colspan="2" class="text-end">Total</th>
                    <th class="text-end">{{ paySvc.totalAmount() | currency }}</th>
                  </tr>
                </tfoot>
              }
            </table>
          </div>
        </section>

        <!-- Waivers summary -->
        @if (hasWaivers()) {
          <section class="mb-4">
            <h6 class="fw-semibold mb-2">Waivers</h6>
            <ul class="list-unstyled mb-0">
              @for (w of waiverSummary(); track w.title) {
                <li class="d-flex align-items-center gap-2 mb-1">
                  @if (w.accepted) {
                    <span class="text-success" aria-label="Accepted">&#10003;</span>
                  } @else {
                    <span class="text-danger" aria-label="Not accepted">&#10007;</span>
                  }
                  <span>{{ w.title }}</span>
                </li>
              }
            </ul>
            @if (state.jobCtx.signatureName()) {
              <div class="small text-muted mt-2">
                Signed by: <strong>{{ state.jobCtx.signatureName() }}</strong>
                @if (state.jobCtx.signatureRole()) { ({{ state.jobCtx.signatureRole() }}) }
              </div>
            }
          </section>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewStepComponent {
    readonly advance = output<void>();
    readonly state = inject(PlayerWizardStateService);
    readonly paySvc = inject(PaymentV2Service);
    private readonly teamService = inject(TeamService);
    private readonly jobService = inject(JobService);

    selectedPlayers() {
        return this.state.familyPlayers.familyPlayers()
            .filter(p => p.selected || p.registered)
            .map(p => ({
                userId: p.playerId,
                name: `${p.firstName} ${p.lastName}`.trim(),
                dob: p.dob || null,
                gender: p.gender || null,
            }));
    }

    getTeamsForPlayer(playerId: string): string[] {
        const teams = this.state.eligibility.selectedTeams()[playerId];
        if (!teams) return [];
        const allTeams = this.teamService.filterByEligibility(null);
        if (Array.isArray(teams)) {
            return teams.map((tid: string) => {
                const team = allTeams.find(t => t.teamId === tid);
                return team?.teamName || tid;
            });
        }
        const team = allTeams.find(t => t.teamId === teams);
        return [team?.teamName || teams];
    }

    getAmountForPlayer(playerId: string): number | null {
        const li = this.paySvc.lineItems().find(i => i.playerId === playerId);
        return li ? li.amount : null;
    }

    isMultiTeamMode(): boolean {
        const t = (this.state.eligibility.teamConstraintType() || '').toUpperCase();
        return !t || t === 'BYCLUBNAME';
    }

    hasWaivers(): boolean {
        return this.state.jobCtx.waiverDefinitions().length > 0;
    }

    waiverSummary(): { title: string; accepted: boolean }[] {
        return this.state.jobCtx.waiverDefinitions().map(d => ({
            title: d.title,
            accepted: this.state.jobCtx.isWaiverAccepted(d.id),
        }));
    }
}
