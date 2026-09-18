import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { PaymentV2Service, type LineItem } from '../state/payment-v2.service';
import { TeamService } from '@views/registration/player/services/team.service';
import { JobService } from '@infrastructure/services/job.service';

/**
 * Review step — summary table of players, teams, and amounts.
 * Waiver summary and server validation errors display.
 *
 * AR-096: the screen's purpose is "these are the details I'm about to pay for" (Todd, 09-18),
 * and everything on it is stated in that tense. Whether a payment is actually coming is one
 * decision — hasAmountDue() — expressed at every level: the hero, each line's badge, the
 * total, and where Continue leads. A parent with nothing left to pay is not "Almost There";
 * they are done, and the screen says so rather than showing them a price they have settled.
 */

/** AR-096 line states. See lineStatus() for why the precedence is what it is. */
export type ReviewLineStatus = 'unpriced' | 'plan' | 'paid' | 'due';
@Component({
    selector: 'app-prw-review-step',
    standalone: true,
    imports: [CurrencyPipe, DatePipe],
    template: `
    <div class="review-shell">
      <!-- Centered hero -->
      <!-- AR-096 — the hero states which of the two screens this is. "Almost There /
           then proceed to payment" is the voice of a coming payment; a family whose
           registrations are already settled is not almost anywhere, and telling them so
           beside a price they have already paid is the confusion Ann reported. -->
      <div class="welcome-hero">
        @if (hasAmountDue()) {
          <h4 class="welcome-title"><i class="bi bi-clipboard-check welcome-icon" style="color: var(--bs-success)"></i> Almost There!</h4>
          <p class="welcome-desc">
            <i class="bi bi-eye me-1"></i>Review your details
            <span class="desc-dot"></span>
            <i class="bi bi-arrow-right me-1"></i>Then proceed to payment
          </p>
        } @else {
          <h4 class="welcome-title"><i class="bi bi-check2-circle welcome-icon" style="color: var(--bs-success)"></i> You're All Set</h4>
          <!-- ⛔ NO MONEY CLAIM HERE, DELIBERATELY. This state is reached whenever the
               checkout collects nothing — which includes an ARB family, and baseTotal()
               excludes ARB lines outright (billableLineItems filters !arbEnrolled), so a
               family BEHIND on a failed draft lands here too. "Nothing left to pay" and
               "nothing to pay today" are both false for them. The per-line badges own
               every money statement on this screen; the hero says only what this screen
               is. Do not reintroduce a balance claim here. -->
          <p class="welcome-desc">
            <i class="bi bi-eye me-1"></i>Review your details
          </p>
        }
      </div>

      <!-- Server validation errors -->
      @if (state.jobCtx.hasServerValidationErrors()) {
        <div class="review-alert">
          <i class="bi bi-exclamation-triangle-fill"></i>
          <div>
            <div class="fw-semibold mb-1">Validation Errors</div>
            <ul class="mb-0 ps-3">
              @for (err of state.jobCtx.getServerValidationErrors(); track err.field) {
                <li>{{ err.message || err.field }}</li>
              }
            </ul>
          </div>
        </div>
      }

      <!-- Players & Teams -->
      <div class="review-section">
        <div class="review-section-header">
          <i class="bi bi-people-fill"></i>
          <span>Players &amp; Teams</span>
        </div>
        <div class="review-section-body">
          @for (player of selectedPlayers(); track player.userId; let last = $last) {
            <div class="review-player-row" [class.border-bottom]="!last">
              <div class="review-player-top">
                <div class="review-player-info">
                  <span class="review-player-name">
                    {{ player.name }}
                    <!-- AR-096: this row is a PRIOR registration, not one being created now.
                         Says "registered", never "paid" — the wizard carries no payment state
                         at this step and must not imply one. -->
                    @if (player.registered) {
                      <span class="review-player-registered">Already registered</span>
                    }
                  </span>
                  @if (player.dob || player.gender) {
                    <span class="review-player-meta">
                      @if (player.gender) { {{ genderLabel(player.gender) }} }
                      @if (player.gender && player.dob) { &middot; }
                      @if (player.dob) { DOB: {{ player.dob | date:'mediumDate' }} }
                    </span>
                  }
                </div>
                @if (!(state.jobCtx.isCacMode() && getLineItemsForPlayer(player.userId).length > 1)) {
                  <div class="review-player-amount">
                    @if (isPlayerFeeUnconfigured(player.userId)) {
                      <span class="review-fee-label">Registration Fee</span>
                      <span class="review-fee-unset"><i class="bi bi-exclamation-triangle me-1"></i>Fee not set</span>
                    } @else if (getBaseFeeForPlayer(player.userId) !== null) {
                      <span class="review-fee-label">Registration Fee</span>
                      {{ getBaseFeeForPlayer(player.userId) | currency }}
                      <!-- AR-096 — the price keeps its place; the badge supplies the word the
                           parent was guessing. The figure above is the PRICE, and the badge
                           says what has happened to it. -->
                      <span class="review-status" [class]="'review-status--' + statusForPlayer(player.userId)">
                        @switch (statusForPlayer(player.userId)) {
                          @case ('paid') { <i class="bi bi-check-circle-fill"></i>Paid }
                          @case ('plan') { <i class="bi bi-calendar2-check"></i>On payment plan }
                          @case ('due') { <i class="bi bi-arrow-right-circle"></i>About to pay }
                        }
                      </span>
                    } @else {
                      <span class="text-muted">&ndash;</span>
                    }
                  </div>
                }
              </div>
              @if (state.jobCtx.isCacMode() && getLineItemsForPlayer(player.userId).length > 1) {
                <ul class="review-events-list review-events-list--priced">
                  @for (li of getLineItemsForPlayer(player.userId); track li.teamId) {
                    <li>
                      <span class="event-name">{{ getEventLabel(li.teamId) }}</span>
                      @if (li.feeConfigured === false) {
                        <span class="event-fee event-fee--unset"><i class="bi bi-exclamation-triangle me-1"></i>Fee not set</span>
                      } @else {
                        <span class="event-fee">{{ li.feeBase | currency }}</span>
                        <!-- AR-096 — per EVENT, not per player: on a camps &amp; clinics
                             registration one child can hold a settled event and a new one
                             at the same time, and the whole complaint is not being able to
                             tell them apart. -->
                        <span class="review-status" [class]="'review-status--' + lineStatus(li)">
                          @switch (lineStatus(li)) {
                            @case ('paid') { <i class="bi bi-check-circle-fill"></i>Paid }
                            @case ('plan') { <i class="bi bi-calendar2-check"></i>On payment plan }
                            @case ('due') { <i class="bi bi-arrow-right-circle"></i>About to pay }
                          }
                        </span>
                      }
                    </li>
                  }
                </ul>
                @if (selectedPlayers().length > 1) {
                  <div class="review-player-total-row">
                    <span>Player Total</span>
                    <span class="review-player-total-amount">{{ getPlayerTotal(player.userId) | currency }}</span>
                  </div>
                }
              } @else {
                <div class="review-player-teams">
                  @for (t of getTeamEntriesForPlayer(player.userId); track t.teamId) {
                    <span class="review-team-pill">
                      @if (state.jobCtx.isCacMode()) {
                        <!-- CAC single event — DIVISION disambiguates here, not agegroup
                             (AM-086); matches the label the 2+ list above uses and the
                             Select Events card the parent came from. -->
                        <span class="team-seg team-name">{{ getEventLabel(t.teamId) }}</span>
                      } @else {
                        @if (t.club) { <span class="team-seg team-club">{{ t.club }}</span> }
                        @if (t.showAgegroup) { <span class="team-seg team-age">{{ t.agegroup }}</span> }
                        <span class="team-seg team-name">{{ t.team }}</span>
                      }
                    </span>
                  }
                </div>
              }
            </div>
          }
          <!-- AR-096 — the footer is the CHARGE, not the catalogue price. Each line above
               still shows its price; this one figure is what the next screen takes, and it
               comes from paySvc.baseTotal() — the same ExpectedTotal the backend recomputes
               and refuses on drift — so the number promised here is the number charged.
               Already-settled and ARB-financed lines are not in it, which is the whole point.
               Nothing to pay renders no row at all: the hero has already said so, and a
               "$0.00" total is one more figure for a parent to misread.
               The old "payments already made are applied at checkout" note is gone — the
               per-line Paid badges say it where the money is, instead of as a footnote. -->
          @if (hasAmountDue()) {
            <div class="review-total-row">
              <span>About to pay</span>
              <span class="review-total-amount">{{ amountDue() | currency }}</span>
            </div>
          }
        </div>
      </div>

      <!-- eCheck heads-up (informational — the method is chosen on the next step) -->
      <!-- AR-096 — gated on the CHARGE, not on the catalogue price. It used to key off
           baseFeeTotal(), which is non-zero even when every line is already settled, so a
           family with nothing to pay was offered advice on how to pay it. -->
      @if (state.jobCtx.bEnableEcheck() && hasAmountDue()) {
        <div class="review-echeck-note">
          <i class="bi bi-bank"></i>
          <div>
            <div class="fw-semibold mb-1">Paying by eCheck?</div>
            <div>eCheck payments are drafted from your bank account and typically finalize
              within <strong>3–5 business days</strong>. Your registration is complete at checkout.</div>
          </div>
        </div>
      }

      <!-- Form details per player -->
      @for (player of selectedPlayers(); track player.userId) {
        @if (getFormFields(player.userId).length > 0) {
          <div class="review-section">
            <div class="review-section-header">
              <i class="bi bi-person-lines-fill"></i>
              <span>{{ player.name }}</span>
            </div>
            <div class="review-section-body">
              <div class="review-fields-grid">
                @for (f of getFormFields(player.userId); track f.name) {
                  <div class="review-field">
                    <span class="review-field-label">{{ f.label }}</span>
                    <span class="review-field-value" [class.text-muted]="!f.value">{{ f.value || '—' }}</span>
                  </div>
                }
              </div>
            </div>
          </div>
        }
      }
    </div>
  `,
    styles: [`
      .review-shell {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
      }

      /* ── Alert ────────────────────────────────────────── */
      .review-alert {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-3);
        border-radius: var(--radius-md);
        background: rgba(var(--bs-danger-rgb), 0.08);
        border: 1px solid rgba(var(--bs-danger-rgb), 0.25);
        color: var(--bs-danger);
        font-size: var(--font-size-sm);

        i { font-size: var(--font-size-lg); flex-shrink: 0; margin-top: 2px; }
      }

      /* ── eCheck pending heads-up ──────────────────────── */
      .review-echeck-note {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-3);
        border-radius: var(--radius-md);
        background: color-mix(in srgb, var(--bs-primary) 8%, transparent);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 25%, transparent);
        color: var(--brand-text);
        font-size: var(--font-size-sm);

        i { font-size: var(--font-size-lg); flex-shrink: 0; margin-top: 2px; color: var(--bs-primary); }
      }

      /* ── Section card ─────────────────────────────────── */
      .review-section {
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        overflow: hidden;
        box-shadow: var(--shadow-sm);
      }

      .review-section-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-body-color-rgb), 0.03);
        border-bottom: 1px solid var(--border-color);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
        text-transform: uppercase;
        letter-spacing: 0.03em;

        i { color: var(--bs-primary); font-size: var(--font-size-base); }
      }

      .review-section-body {
        padding: 0;
      }

      /* ── Player rows ──────────────────────────────────── */
      .review-player-row {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        padding: var(--space-3);

        &.border-bottom { border-bottom: 1px solid var(--border-color); }
      }

      .review-player-top {
        display: flex;
        align-items: flex-start;
        justify-content: space-between;
        gap: var(--space-3);
      }

      .review-player-info {
        display: flex;
        flex-direction: column;
        gap: 1px;
        min-width: 0;
      }

      .review-player-name {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
      }

      .review-player-meta {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      /* AR-096 — marks a row that already exists. Carries its own text, not colour
         alone, so it survives a monochrome palette and a screen reader. */
      .review-player-registered {
        display: inline-block;
        margin-left: var(--space-2);
        padding: 0 var(--space-2);
        border: 1px solid var(--bs-success);
        border-radius: var(--radius-pill, 999px);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
        vertical-align: middle;
      }

      .review-player-teams {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
      }

      .review-team-pill {
        display: inline-flex;
        align-items: baseline;
        flex-wrap: wrap;
        gap: var(--space-1);
        font-size: var(--font-size-sm);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.2);
      }

      /* Segment separators: a middot before every segment after the first. */
      .team-seg + .team-seg::before {
        content: '·';
        margin-right: var(--space-1);
        opacity: 0.55;
      }

      .team-club { font-weight: var(--font-weight-bold); }
      .team-age { opacity: 0.85; }
      .team-name { font-weight: var(--font-weight-semibold); }

      .review-events-list {
        list-style: none;
        margin: 0;
        padding: 0 0 0 var(--space-4);
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);

        li {
          padding: 2px 0;
          &::before {
            content: '•';
            color: var(--bs-primary);
            margin-right: var(--space-2);
          }
        }
      }

      .review-events-list--priced {
        font-size: var(--font-size-sm);
        color: var(--brand-text);

        li {
          display: flex;
          align-items: baseline;
          gap: var(--space-2);
          padding: 2px 0;
        }

        .event-name { flex: 1 1 auto; min-width: 0; }
        .event-fee {
          flex: 0 0 auto;
          font-weight: var(--font-weight-semibold);
          white-space: nowrap;
        }
      }

      .review-player-total-row {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-top: var(--space-1);
        padding: var(--space-1) 0 0 var(--space-4);
        border-top: 1px dashed var(--border-color);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      .review-player-total-amount {
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
      }

      .review-player-amount {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        text-align: right;
      }

      .review-fee-label {
        display: block;
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-normal);
        color: var(--text-muted);
      }

      /* AR-096 — the word that says what happened to the figure beside it. Text + icon,
         never colour alone: the three states must survive a monochrome palette and a
         screen reader, same rule as .review-player-registered above. Tinted fill rather
         than an outline so it reads as a state stamped ON the amount, where the outlined
         "Already registered" pill beside the NAME is about the registration. */
      .review-status {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        margin-top: var(--space-1);
        padding: 0 var(--space-2);
        border-radius: var(--radius-pill, 999px);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
      }

      .review-status--paid {
        background: var(--bs-success-bg-subtle);
        color: var(--bs-success-text-emphasis);
      }

      /* Owed but never charged here — carried by a live ARB subscription. Deliberately NOT
         the success colour: it is not settled, and not the amount about to be taken either. */
      .review-status--plan {
        background: var(--bs-info-bg-subtle);
        color: var(--bs-info-text-emphasis);
      }

      .review-status--due {
        background: var(--bs-primary-bg-subtle);
        color: var(--bs-primary-text-emphasis);
      }

      /* Fee unconfigured — pre-existing orphan registration; can't be priced or charged. */
      .review-fee-unset,
      .event-fee--unset {
        color: var(--bs-danger);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
      }

      /* ── Total row ────────────────────────────────────── */
      .review-total-row {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-body-color-rgb), 0.03);
        border-top: 2px solid var(--border-color);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      /* AR-096 — sits under the total, not beside it, so it reads as a footnote to the
         figure rather than a second amount. */
      .review-total-amount {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-bold);
        color: var(--bs-success);
      }

      /* ── Form fields grid ────────────────────────────── */
      .review-fields-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 0;
      }

      .review-field {
        display: flex;
        flex-direction: column;
        gap: 1px;
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid var(--border-color);

        &:nth-child(odd) { border-right: 1px solid var(--border-color); }
      }

      .review-field-label {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: var(--brand-text-muted);
      }

      .review-field-value {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
      }

      /* ── Mobile ───────────────────────────────────────── */
      @media (max-width: 575.98px) {
        .review-hero { padding: var(--space-3); gap: var(--space-2); }
        .review-hero-icon { font-size: 1.5rem; }
        .review-hero-title { font-size: var(--font-size-base); }

        .review-player-row {
          grid-template-columns: 1fr;
          gap: var(--space-1);
          padding: var(--space-2) var(--space-3);
        }

        .review-player-amount { text-align: right; }

        .review-fields-grid { grid-template-columns: 1fr; }
        .review-field { border-right: none !important; }
      }
    `],
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
                // AR-096 — the filter above admits rows on either flag, so the template
                // could not tell a new selection from one that already exists.
                registered: p.registered,
            }));
    }

    /**
     * Composed team lines for the review summary: club, age group, and team name.
     *
     * The club segment comes from the team's ClubrepRegistrationid → Registrations.ClubName
     * route (surfaced on the DTO as clubName). A team with no ClubrepRegistrationid has no
     * club name, so the segment is simply omitted — clubless events show age group + team only.
     *
     * Team name uses getTeamDisplayName (the real stored name; a player waitlisted at payment
     * lands on the twin team whose name already is "WAITLIST - {name}"). The age-group segment
     * is dropped when it would just repeat the team name (e.g. a team literally named "2029").
     */
    /**
     * CAC event label — "Division: Team Name" (AM-086). Mirrors getEventLabel in
     * player-forms-step so Player Details and Review can't print the same event differently.
     *
     * DIVISION, not agegroup: on a Camps & Clinics job the Select Events card prints
     * `divisionName` under the team name, and that is what separates two same-named events
     * (Ann's example: two "Test 1" teams, split only by "Draw Control Training" vs "AIM Spring
     * Train & Play"). The clubName:agegroupName:teamName identity below belongs to NON-CAC jobs.
     */
    getEventLabel(teamId: string): string {
        const team = this.teamService.getTeamById(teamId);
        const name = this.teamService.getTeamDisplayName(teamId);
        const division = team?.divisionName?.trim();
        return division ? `${division}: ${name}` : name;
    }

    getTeamEntriesForPlayer(
        playerId: string
    ): { teamId: string; club: string | null; agegroup: string | null; team: string; showAgegroup: boolean }[] {
        const teams = this.state.eligibility.selectedTeams()[playerId] ?? [];
        return teams.map(tid => {
            const t = this.teamService.getTeamById(tid);
            const team = this.teamService.getTeamDisplayName(tid);
            const club = (t?.clubName || '').trim() || null;
            const agegroup = (t?.agegroupName || '').trim() || null;
            const showAgegroup = !!agegroup && agegroup.toLowerCase() !== team.trim().toLowerCase();
            return { teamId: tid, club, agegroup, team, showAgegroup };
        });
    }

    /**
     * AR-096 — the money state of one Review line.
     *
     * Ann reopened this because a bare figure "shows whether paid or owed" and the parent
     * reads it as a demand either way. The figure was never the problem: what was missing is
     * the word that says which kind of number it is. These four states supply it.
     *
     * The state comes from the line's own server financials — `lineItems()` already carries
     * `amount` (what this submission charges), `arbEnrolled` and `feeConfigured`. Note the
     * contrast with the "Already registered" pill beside the player's name: that keys off
     * SELECTION, which is why it correctly says registered and never paid. This keys off
     * MONEY, so it can say paid.
     *
     * Order matters, and it is not arbitrary:
     * - `unpriced` first — a line with no configured fee cannot be charged at all, so no
     *   money word applies to it. It is an error state that happens to live in the price slot.
     * - `plan` before `amount` — an ARB line is owed but is dropped from every charge path
     *   server-side (PaymentService.PartitionArbEnrolled), so it is neither paid NOR about to
     *   be paid. Calling it either would put the screen out of step with the charge.
     */
    lineStatus(li: LineItem): ReviewLineStatus {
        if (li.feeConfigured === false) return 'unpriced';
        if (li.arbEnrolled) return 'plan';
        return li.amount > 0 ? 'due' : 'paid';
    }

    /** Line state for the single-line (non-CAC-multi) row. */
    statusForPlayer(playerId: string): ReviewLineStatus | null {
        const li = this.paySvc.lineItems().find(i => i.playerId === playerId);
        return li ? this.lineStatus(li) : null;
    }

    /**
     * Is a payment actually coming? The one question the whole screen turns on.
     *
     * `baseTotal` is the canonical charge basis — the client figure the backend recomputes
     * and refuses on drift (ExpectedTotal), already net of ARB lines. Deriving the screen
     * from it rather than from a local sum is what guarantees the total shown here is the
     * total charged next, which is the failure that would make this change worse than the
     * confusion it replaces.
     */
    hasAmountDue(): boolean {
        return this.paySvc.baseTotal() > 0;
    }

    amountDue(): number {
        return this.paySvc.baseTotal();
    }

    getBaseFeeForPlayer(playerId: string): number | null {
        const li = this.paySvc.lineItems().find(i => i.playerId === playerId);
        return li ? li.feeBase : null;
    }

    /** True when the player's (single) line has no configured fee — render "Fee not set"
     *  rather than a currency value (only reachable for pre-existing orphan registrations;
     *  new selections are blocked upstream at team selection). */
    isPlayerFeeUnconfigured(playerId: string): boolean {
        const li = this.paySvc.lineItems().find(i => i.playerId === playerId);
        return li?.feeConfigured === false;
    }

    getLineItemsForPlayer(playerId: string) {
        return this.paySvc.lineItems().filter(i => i.playerId === playerId);
    }

    getPlayerTotal(playerId: string): number {
        return this.getLineItemsForPlayer(playerId).reduce((sum, li) => sum + li.feeBase, 0);
    }

    genderLabel(g: string): string {
        const v = (g || '').trim().toUpperCase();
        if (v === 'F') return 'Female';
        if (v === 'M') return 'Male';
        return g;
    }

    isMultiTeamMode(): boolean {
        const t = (this.state.eligibility.teamConstraintType() || '').toUpperCase();
        return !t || t === 'BYCLUBNAME';
    }

    getFormFields(playerId: string): { name: string; label: string; value: string; required: boolean }[] {
        const schemas = this.state.jobCtx.profileFieldSchemas();
        return schemas
            .filter(f => this.state.isFieldVisibleForPlayer(playerId, f))
            .map(f => {
                const raw = this.state.playerForms.getPlayerFieldValue(playerId, f.name);
                let value = '';
                if (raw == null) value = '';
                else if (Array.isArray(raw)) value = raw.join(', ');
                else if (typeof raw === 'boolean') value = raw ? 'Yes' : 'No';
                else value = String(raw).trim();
                return { name: f.name, label: f.label, value, required: !!f.required };
            })
;
    }
}
