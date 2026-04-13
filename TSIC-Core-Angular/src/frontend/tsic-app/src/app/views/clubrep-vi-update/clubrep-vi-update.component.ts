import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnDestroy, ViewChild, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { environment } from '@environments/environment';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { LoginComponent } from '@views/auth/login/login.component';
import { CreditCardFormComponent } from '@views/registration/shared/components/credit-card-form.component';
import { TeamInsuranceService } from '@views/registration/team/services/team-insurance.service';
import type { AuthTokenResponse, CreditCardInfo, SetWizardContextRequest } from '@core/api';
import type { VIOfferData, VIQuoteObject } from '@views/registration/shared/types/wizard.types';

type ViewState = 'login' | 'loading' | 'offer' | 'nothing' | 'success' | 'error';

/**
 * Post-registration team-insurance re-entry page. Reached via `/{jobPath}/ClubRepVIUpdate`
 * — the URL given to club reps in admin/director communications when they didn't buy
 * team RegSaver coverage at registration time. Mirrors PlayerVIUpdate for families.
 *
 * Flow: club rep signs in (password required, legacy parity) → POST set-clubrep-context
 * mints a regId-bearing JWT for their existing ClubRep registration → GET team/pre-submit
 * builds the offer over their teams that lack a `ViPolicyId` → widget-driven purchase.
 */
@Component({
    selector: 'app-clubrep-vi-update',
    standalone: true,
    imports: [CurrencyPipe, RouterLink, LoginComponent, CreditCardFormComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    styles: [`
      .status-hero {
        display: flex;
        flex-direction: column;
        align-items: center;
        text-align: center;
        padding: var(--space-8) var(--space-4) var(--space-6);
        border-radius: var(--radius-lg, 1rem);
        background: linear-gradient(180deg,
          color-mix(in srgb, var(--hero-accent) 8%, transparent) 0%,
          color-mix(in srgb, var(--hero-accent) 2%, transparent) 60%,
          transparent 100%);
      }
      .status-hero-badge {
        width: 5rem;
        height: 5rem;
        border-radius: 50%;
        display: grid;
        place-items: center;
        margin-bottom: var(--space-4);
        color: #fff;
        font-size: 2.25rem;
        background: linear-gradient(135deg,
          var(--hero-accent) 0%,
          color-mix(in srgb, var(--hero-accent) 70%, black) 100%);
        box-shadow:
          0 10px 24px -8px color-mix(in srgb, var(--hero-accent) 55%, transparent),
          inset 0 1px 0 rgba(255, 255, 255, 0.25);
        transition: transform 200ms ease;
      }
      .status-hero-badge:hover { transform: scale(1.03); }
      .status-hero-title {
        font-weight: 700;
        margin-bottom: var(--space-2);
        color: var(--brand-text, inherit);
        letter-spacing: -0.01em;
      }
      .status-hero-body {
        max-width: 46ch;
        color: var(--bs-secondary-color, #6c757d);
        margin-bottom: var(--space-5);
        line-height: 1.55;
      }
      .status-hero-cta {
        min-width: 14rem;
        font-weight: 600;
      }
      .status-hero-cta:focus-visible {
        outline: none;
        box-shadow: var(--shadow-focus);
      }
      .status-hero--info { --hero-accent: var(--bs-primary); }
      .status-hero--success { --hero-accent: var(--bs-success); }
      .status-hero--danger { --hero-accent: var(--bs-danger); }
      @media (prefers-reduced-motion: reduce) {
        .status-hero-badge { transition: none; }
        .status-hero-badge:hover { transform: none; }
      }
    `],
    template: `
      <div class="container py-4" style="max-width: 760px;">
        <div class="card shadow border-0 card-rounded">
          <div class="card-body">
            <h4 class="mb-3">
              <i class="bi bi-shield-check me-2 text-primary"></i>
              Team Registration Protection
            </h4>

            @switch (state()) {
              @case ('login') {
                <p class="wizard-tip mb-3">
                  Sign in with your club rep account to add Registration Protection
                  to teams you've already registered.
                </p>
                @if (loginWarning()) {
                  <div class="alert alert-warning border-0" role="alert">
                    <i class="bi bi-exclamation-triangle-fill me-2"></i>
                    {{ loginWarning() }}
                  </div>
                }
                <app-login
                  [theme]="''"
                  [embedded]="true"
                  [headerText]="'Club Rep Sign In'"
                  [subHeaderText]="'Enter your username and password'"
                  (loginSuccess)="onLoginSuccess()" />
              }

              @case ('loading') {
                <div class="text-center py-4">
                  <div class="spinner-border text-primary" role="status"></div>
                  <div class="mt-3 text-muted">Checking your team registrations…</div>
                </div>
              }

              @case ('nothing') {
                <div class="status-hero status-hero--info" role="status">
                  <div class="status-hero-badge">
                    <i class="bi bi-shield-check"></i>
                  </div>
                  <h5 class="status-hero-title">Your teams are all covered</h5>
                  <p class="status-hero-body">
                    Every team you registered for this event either already has
                    Registration Protection, or isn't eligible. Nothing to purchase today.
                  </p>
                  <a class="btn btn-primary status-hero-cta" [routerLink]="['/', jobPath()]">
                    <i class="bi bi-house-door-fill me-2"></i>Return to event home
                  </a>
                </div>
              }

              @case ('offer') {
                <p class="wizard-tip mb-3">
                  Select Registration Protection for each team you'd like to cover,
                  then enter payment information.
                </p>

                <div class="mb-3">
                  <div #viOffer id="dVITeamOffer" class="text-center"></div>
                  @if (teamInsurance.widgetInitialized() && !teamInsurance.hasUserResponse()) {
                    <div class="alert alert-secondary border-0 py-2 small" role="alert">
                      Please indicate your interest in Registration Protection for each
                      team listed.
                    </div>
                  }
                </div>

                @if (hasSelectedQuotes()) {
                  <div class="alert alert-info border-0" role="status">
                    Premium total: <strong>{{ premiumTotal() | currency }}</strong>
                  </div>

                  <app-credit-card-form
                    (validChange)="ccValid.set($event)"
                    (valueChange)="ccValue.set($event)"
                    [viOnly]="true" />

                  <div class="d-flex justify-content-between gap-2 mt-4">
                    <a class="btn btn-outline-secondary" [routerLink]="['/', jobPath()]">
                      Cancel
                    </a>
                    <button type="button"
                            class="btn btn-primary"
                            [disabled]="!ccValid() || purchasing()"
                            (click)="purchase()">
                      @if (purchasing()) {
                        <span class="spinner-border spinner-border-sm me-2"></span>
                      }
                      Purchase Registration Protection
                    </button>
                  </div>
                } @else if (teamInsurance.hasUserResponse()) {
                  <div class="alert alert-warning border-0" role="status">
                    You've declined Registration Protection for all teams. Choose at least
                    one team above to continue, or return to the event home.
                  </div>
                  <div class="text-center mt-3">
                    <a class="btn btn-outline-secondary" [routerLink]="['/', jobPath()]">
                      Return to event home
                    </a>
                  </div>
                }
              }

              @case ('success') {
                <div class="status-hero status-hero--success" role="status">
                  <div class="status-hero-badge">
                    <i class="bi bi-check2-circle"></i>
                  </div>
                  <h5 class="status-hero-title">Protection purchased</h5>
                  <p class="status-hero-body">
                    Your team Registration Protection is active. Vertical Insure will email
                    a policy confirmation shortly.
                  </p>
                  <a class="btn btn-primary status-hero-cta" [routerLink]="['/', jobPath()]">
                    <i class="bi bi-house-door-fill me-2"></i>Return to event home
                  </a>
                </div>
              }

              @case ('error') {
                <div class="status-hero status-hero--danger" role="alert">
                  <div class="status-hero-badge">
                    <i class="bi bi-exclamation-triangle-fill"></i>
                  </div>
                  <h5 class="status-hero-title">Something went wrong</h5>
                  <p class="status-hero-body">
                    {{ errorMsg() || 'We couldn\\'t complete that action. Please try again in a moment.' }}
                  </p>
                  <a class="btn btn-outline-secondary status-hero-cta" [routerLink]="['/', jobPath()]">
                    Return to event home
                  </a>
                </div>
              }
            }
          </div>
        </div>
      </div>
    `,
})
export class ClubRepVIUpdateComponent implements OnDestroy {
    @ViewChild('viOffer') viOfferRef?: ElementRef<HTMLElement>;

    readonly teamInsurance = inject(TeamInsuranceService);
    private readonly auth = inject(AuthService);
    private readonly http = inject(HttpClient);
    private readonly route = inject(ActivatedRoute);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly state = signal<ViewState>('login');
    readonly errorMsg = signal<string | null>(null);
    readonly loginWarning = signal<string | null>(null);
    readonly jobPath = signal<string>('');
    readonly purchasing = signal(false);
    readonly ccValid = signal(false);
    readonly ccValue = signal<Record<string, string> | null>(null);

    readonly hasSelectedQuotes = computed(() =>
        this.teamInsurance.hasUserResponse() && this.teamInsurance.quotes().length > 0,
    );

    readonly premiumTotal = computed(() => this.teamInsurance.premiumTotal());

    private viInitRetries = 0;
    private viInitTimeout?: ReturnType<typeof setTimeout>;

    constructor() {
        const jp = this.route.parent?.snapshot.paramMap.get('jobPath')
            ?? this.route.snapshot.paramMap.get('jobPath')
            ?? '';
        this.jobPath.set(jp);

        // Legacy parity: unconditionally sign out any authenticated user on entry.
        // The email link is a persistent attack surface — we always want an explicit
        // password re-challenge before building an insurance offer.
        if (this.auth.isAuthenticated()) {
            this.auth.logoutLocal();
        }
    }

    ngOnDestroy(): void {
        clearTimeout(this.viInitTimeout);
        this.teamInsurance.reset();
    }

    onLoginSuccess(): void {
        // Phase 1 /auth/login token carries no role claim. Role-gating is enforced
        // server-side by set-clubrep-context, which 400s when no ClubRep registration
        // exists for (userId, jobId). The 400 message becomes the inline warning.
        this.loginWarning.set(null);
        this.upgradeTokenAndLoadOffer();
    }

    purchase(): void {
        if (this.purchasing()) return;
        const cc = this.ccValue();
        if (!cc) {
            this.toast.show('Credit card information is required.', 'danger', 3000);
            return;
        }
        const quotes = this.teamInsurance.quotes();
        const quoteIds = quotes.map(q => String(q?.quote_id ?? '')).filter(Boolean);
        const teamIds = quotes.map(q => this.extractTeamId(q)).filter(Boolean);
        if (quoteIds.length === 0 || quoteIds.length !== teamIds.length) {
            this.toast.show('Insurance quote / team mismatch. Please retry.', 'danger', 4000);
            return;
        }

        const card: CreditCardInfo = {
            number: cc['number']?.trim() || undefined,
            expiry: cc['expiry']?.trim() || undefined,
            code: cc['code']?.trim() || undefined,
            firstName: cc['firstName']?.trim() || undefined,
            lastName: cc['lastName']?.trim() || undefined,
            zip: cc['zip']?.trim() || undefined,
            email: cc['email']?.trim() || undefined,
            phone: cc['phone']?.trim() || undefined,
            address: cc['address']?.trim() || undefined,
        };

        this.purchasing.set(true);
        this.teamInsurance.purchaseTeamInsurance(teamIds, quoteIds, card)
            .then(result => {
                this.purchasing.set(false);
                if (result.success) {
                    this.state.set('success');
                } else {
                    this.errorMsg.set(result.error || 'Insurance purchase failed.');
                    this.state.set('error');
                }
            });
    }

    private upgradeTokenAndLoadOffer(): void {
        this.state.set('loading');
        const body: SetWizardContextRequest = { jobPath: this.jobPath() };
        this.http.post<AuthTokenResponse>(`${environment.apiUrl}/team-registration/set-clubrep-context`, body)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: resp => {
                    if (resp.accessToken) this.auth.applyNewToken(resp.accessToken);
                    this.fetchOffer();
                },
                error: err => {
                    const status = err?.status as number | undefined;
                    const serverMsg = (err?.error?.message as string | undefined) || '';
                    // 400 from set-clubrep-context = no ClubRep registration for this event.
                    // Keep the login form visible so the user can retry with the correct account.
                    if (status === 400) {
                        this.auth.logoutLocal();
                        this.loginWarning.set(serverMsg || 'No team registration found for this event under that account.');
                        this.state.set('login');
                        return;
                    }
                    this.errorMsg.set(serverMsg || 'Could not verify your club rep account for this event.');
                    this.state.set('error');
                },
            });
    }

    private async fetchOffer(): Promise<void> {
        const offer = await this.teamInsurance.fetchTeamInsuranceOffer();
        if (!offer || !offer.available || !offer.teamObject) {
            this.state.set('nothing');
            return;
        }
        this.state.set('offer');
        this.viInitTimeout = setTimeout(() => this.tryInitWidget(offer.teamObject as VIOfferData), 0);
    }

    private tryInitWidget(offerData: VIOfferData): void {
        if (!this.viOfferRef) {
            if (this.viInitRetries++ < 20) {
                this.viInitTimeout = setTimeout(() => this.tryInitWidget(offerData), 150);
            }
            return;
        }
        this.viInitRetries = 0;
        this.teamInsurance.initWidget('#dVITeamOffer', offerData);
    }

    private extractTeamId(q: VIQuoteObject): string {
        const meta = q?.metadata as Record<string, unknown> | undefined;
        if (!meta) return '';
        // Backend sets metadata.tsic_teamid (lowercase, snake_case) per VITeamMetadataDto.
        const direct = meta['tsic_teamid'] ?? meta['tsicTeamId'] ?? meta['TsicTeamId'];
        return direct ? String(direct) : '';
    }
}
