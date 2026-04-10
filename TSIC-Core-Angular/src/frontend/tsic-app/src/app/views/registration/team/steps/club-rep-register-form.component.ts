import { ChangeDetectionStrategy, Component, DestroyRef, inject, output, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { debounceTime, distinctUntilChanged, filter, switchMap, catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { ClubService } from '@infrastructure/services/club.service';
import { FormFieldDataService, type SelectOption } from '@infrastructure/services/form-field-data.service';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { ClubRepRegistrationRequest, ClubSearchResult } from '@core/api';

/**
 * Club decision state:
 * - 'pending'  — matches found, user hasn't decided yet
 * - 'blocked'  — 85%+ match, registration is blocked (must contact existing rep)
 * - 'new'      — user confirmed "create new" in the 65-84% warning band
 * - 'clear'    — no matches found, auto-approved for new club
 */
type ClubDecision = 'pending' | 'blocked' | 'new' | 'clear';

/**
 * Club rep self-registration modal with two-tier club name validation.
 *
 * Tier 1 (85%+ match)  — HARD BLOCK: club almost certainly exists. Shows
 *   existing rep's contact info so the registrant can reach out directly.
 *   Cannot be bypassed. Prevents hijacking of another club's team library.
 *
 * Tier 2 (65-84% match) — WARNING: similar name. User must explicitly
 *   confirm "create new club" to proceed.
 *
 * Below 65% — no friction, new club created automatically.
 */
@Component({
    selector: 'app-club-rep-register-form',
    standalone: true,
    imports: [ReactiveFormsModule, TsicDialogComponent],
    styles: [`
      /* ── Blocked panel (Tier 1: 85%+) ────────────────────── */
      .club-blocked-panel {
        border: 1px solid rgba(var(--bs-danger-rgb), 0.25);
        border-radius: var(--radius-md);
        margin-top: var(--space-2);
        overflow: hidden;
      }
      .club-blocked-header {
        padding: var(--space-3);
        background: rgba(var(--bs-danger-rgb), 0.06);
        border-bottom: 1px solid rgba(var(--bs-danger-rgb), 0.15);
      }
      .club-blocked-header h6 {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-bold);
        color: var(--bs-danger);
        margin: 0;
      }
      .club-blocked-header p {
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        margin: var(--space-2) 0 0;
        line-height: var(--line-height-normal);
      }
      .blocked-club-card {
        padding: var(--space-3);
        border-bottom: 1px solid rgba(var(--bs-danger-rgb), 0.1);
      }
      .blocked-club-card:last-child { border-bottom: none; }
      .rep-contact {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        margin-top: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-primary-rgb), 0.04);
        border-radius: var(--radius-sm);
        font-size: var(--font-size-sm);
      }
      .rep-contact i { color: var(--bs-primary); flex-shrink: 0; }
      .rep-contact a {
        color: var(--bs-primary);
        text-decoration: none;
        font-weight: var(--font-weight-medium);
      }
      .rep-contact a:hover { text-decoration: underline; }
      .blocked-footer {
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-danger-rgb), 0.03);
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        line-height: var(--line-height-normal);
      }

      /* ── Warning panel (Tier 2: 65-84%) ──────────────────── */
      .club-warning-panel {
        border: 1px solid rgba(var(--bs-warning-rgb), 0.3);
        border-radius: var(--radius-md);
        margin-top: var(--space-2);
        overflow: hidden;
      }
      .club-warning-header {
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-warning-rgb), 0.06);
        border-bottom: 1px solid rgba(var(--bs-warning-rgb), 0.15);
      }
      .club-warning-header h6 {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        margin: 0;
      }
      .club-warning-header p {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        margin: var(--space-1) 0 0;
        line-height: var(--line-height-normal);
      }
      .warning-club-row {
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid var(--border-color);
        font-size: var(--font-size-sm);
      }
      .warning-club-row:last-child { border-bottom: none; }
      .warning-confirm-bar {
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-warning-rgb), 0.03);
        text-align: center;
      }

      /* ── Decision confirmation bars ──────────────────────── */
      .club-decision-bar {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-3);
        border-radius: var(--radius-md);
        margin-top: var(--space-2);
      }
      .club-decision-bar i { margin-top: 2px; flex-shrink: 0; }
      .decision-confirmed {
        background: rgba(var(--bs-success-rgb), 0.06);
        border: 1px solid rgba(var(--bs-success-rgb), 0.15);
      }
      .decision-confirmed i { color: var(--bs-success); }
      .decision-new-info {
        background: rgba(var(--bs-primary-rgb), 0.04);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.12);
      }
      .decision-new-info i { color: var(--bs-primary); }
      .decision-body { flex: 1; min-width: 0; }
      .decision-title {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }
      .decision-detail {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        margin-top: 2px;
        line-height: var(--line-height-normal);
      }

      /* ── Shared ──────────────────────────────────────────── */
      .form-divider { border-color: var(--border-color); opacity: 0.5; }
      .value-prop {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        line-height: var(--line-height-normal);
        padding: var(--space-1) 0;
      }
      .value-prop i { color: var(--bs-success); }
      .match-confidence {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        white-space: nowrap;
      }
      .confidence-high {
        background: rgba(var(--bs-danger-rgb), 0.1);
        color: var(--bs-danger);
      }
      .confidence-medium {
        background: rgba(var(--bs-warning-rgb), 0.12);
        color: var(--bs-warning);
      }
      .mega-club-tag {
        font-size: var(--font-size-xs);
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-info-rgb), 0.1);
        color: var(--bs-info);
      }
    `],
    template: `
    <tsic-dialog [open]="true" size="sm" (requestClose)="closed.emit()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title"><i class="bi bi-shield-plus me-2"></i>Create Club Rep Account</h5>
          <button type="button" class="btn-close" (click)="closed.emit()" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          @if (registrationComplete()) {
            <!-- ── SUCCESS ── -->
            <div class="text-center py-4">
              <i class="bi bi-check-circle-fill text-success" style="font-size: 2.5rem;"></i>
              <h6 class="fw-bold mt-3 mb-2">Account Created!</h6>
              <p class="small text-muted mb-2">
                Your club is set up. When you register for events, you'll select
                teams from your club library instead of re-entering them each time.
              </p>
              <p class="small text-muted mb-0">Close this dialog and sign in with your new credentials.</p>
            </div>
          } @else {
            <form [formGroup]="form" (ngSubmit)="onSubmit()">

              <!-- ═══ CLUB NAME INPUT ═══ -->
              <div class="mb-2">
                <label class="form-label fw-medium small mb-1">Club Name</label>
                <input class="form-control form-control-sm" formControlName="clubName"
                       placeholder="Start typing your club name..."
                       autocomplete="off"
                       [class.is-invalid]="submitted() && form.controls.clubName.invalid" />
                <div class="value-prop mt-1">
                  <i class="bi bi-lightning-charge me-1"></i>
                  Your club keeps a team library across all tournaments administered with TeamSportsInfo — enter team details once, then select from the list at every future event.
                </div>
              </div>

              <!-- Loading -->
              @if (clubSearchLoading()) {
                <div class="text-center py-2">
                  <span class="spinner-border spinner-border-sm text-primary me-1"></span>
                  <span class="small text-muted">Checking for your club...</span>
                </div>
              }

              <!-- ═══ TIER 1: HARD BLOCK (85%+ match) ═══ -->
              @if (blockedMatches().length > 0 && !clubSearchLoading()) {
                <div class="club-blocked-panel">
                  <div class="club-blocked-header">
                    <h6><i class="bi bi-shield-exclamation me-2"></i>This club is already registered</h6>
                    <p>
                      To protect club data, we can't create a duplicate.
                      If this is your club, contact the existing rep below to be added.
                    </p>
                  </div>
                  @for (club of blockedMatches(); track club.clubId) {
                    <div class="blocked-club-card">
                      <div class="d-flex justify-content-between align-items-center">
                        <div>
                          <span class="fw-semibold">{{ club.clubName }}</span>
                          @if (club.state) {
                            <span class="text-muted ms-1 small">({{ club.state }})</span>
                          }
                        </div>
                        <div class="d-flex align-items-center gap-1">
                          @if (club.isRelatedClub) {
                            <span class="mega-club-tag"><i class="bi bi-diagram-3 me-1"></i>Same org</span>
                          }
                          <span class="match-confidence confidence-high">Likely match</span>
                        </div>
                      </div>
                      @if (club.teamCount) {
                        <div class="small text-muted mt-1">
                          {{ club.teamCount }} team{{ club.teamCount === 1 ? '' : 's' }} in library
                        </div>
                      }
                      @if (club.repEmail) {
                        <div class="rep-contact">
                          <i class="bi bi-envelope"></i>
                          <div>
                            @if (club.repName) {
                              <span>{{ club.repName }}</span>
                              <span class="text-muted mx-1">&mdash;</span>
                            }
                            <a [href]="'mailto:' + club.repEmail
                              + '?subject=Request to join ' + encodeURIComponent(club.clubName)
                              + '&body=' + encodeURIComponent(getEmailBody(club))">
                              {{ club.repEmail }}
                            </a>
                          </div>
                        </div>
                      }
                    </div>
                  }
                  <div class="blocked-footer">
                    <i class="bi bi-info-circle me-1"></i>
                    If you believe this is a different club with a similar name, contact
                    the tournament director for assistance.
                  </div>
                </div>
              }

              <!-- ═══ TIER 2: WARNING (65-84% match) ═══ -->
              @if (warningMatches().length > 0 && blockedMatches().length === 0 && !clubSearchLoading() && clubDecision() !== 'new') {
                <div class="club-warning-panel">
                  <div class="club-warning-header">
                    <h6><i class="bi bi-exclamation-triangle me-2 text-warning"></i>Similar clubs on file</h6>
                    <p>
                      These clubs have similar names. If one of them is yours, don't create a
                      duplicate — reach out to the existing rep instead. Duplicate clubs can't
                      share team history.
                    </p>
                  </div>
                  @for (club of warningMatches(); track club.clubId) {
                    <div class="warning-club-row">
                      <div class="d-flex justify-content-between align-items-center">
                        <div>
                          <span class="fw-medium">{{ club.clubName }}</span>
                          @if (club.state) {
                            <span class="text-muted ms-1 small">({{ club.state }})</span>
                          }
                          @if (club.teamCount) {
                            <span class="text-muted small ms-1">&bull; {{ club.teamCount }} teams</span>
                          }
                        </div>
                        <span class="match-confidence confidence-medium">Possible</span>
                      </div>
                      @if (club.repEmail) {
                        <div class="small mt-1">
                          <i class="bi bi-envelope text-primary me-1"></i>
                          @if (club.repName) { <span class="text-muted">{{ club.repName }}: </span> }
                          <a [href]="'mailto:' + club.repEmail" class="text-primary">{{ club.repEmail }}</a>
                        </div>
                      }
                    </div>
                  }
                  <div class="warning-confirm-bar">
                    <button type="button" class="btn btn-sm btn-outline-primary fw-medium"
                            (click)="confirmNewClub()">
                      <i class="bi bi-plus-circle me-1"></i>None of these are my club — create new
                    </button>
                    <div class="small text-muted mt-1">This starts a new club with no prior team history.</div>
                  </div>
                </div>
              }

              <!-- ═══ DECISION: NEW CLUB CONFIRMED (from warning band) ═══ -->
              @if (clubDecision() === 'new' && warningMatches().length > 0) {
                <div class="club-decision-bar decision-new-info">
                  <i class="bi bi-plus-circle-fill"></i>
                  <div class="decision-body">
                    <div class="decision-title">New club: {{ form.controls.clubName.value }}</div>
                    <div class="decision-detail">
                      Starting fresh. Teams you add will carry forward to future events automatically.
                    </div>
                  </div>
                  <button type="button" class="btn btn-sm btn-link text-muted p-0 flex-shrink-0"
                          (click)="resetClubDecision()" aria-label="Go back to club selection">
                    <i class="bi bi-pencil"></i>
                  </button>
                </div>
              }

              <hr class="form-divider my-2">

              <!-- ═══ CREDENTIALS ═══ -->
              <div class="row g-2 mb-2">
                <div class="col-12">
                  <input class="form-control form-control-sm" formControlName="username"
                         placeholder="Username" autocomplete="username"
                         [class.is-required]="!form.controls.username.value?.trim()"
                         [class.is-invalid]="submitted() && form.controls.username.invalid" />
                </div>
                <div class="col-6">
                  <input type="password" class="form-control form-control-sm" formControlName="password"
                         placeholder="Password" autocomplete="new-password"
                         [class.is-required]="!form.controls.password.value"
                         [class.is-invalid]="submitted() && form.controls.password.invalid" />
                </div>
                <div class="col-6">
                  <input type="password" class="form-control form-control-sm" formControlName="confirmPassword"
                         placeholder="Confirm Password" autocomplete="new-password"
                         [class.is-invalid]="submitted() && passwordMismatch()" />
                </div>
                @if (submitted() && passwordMismatch()) {
                  <div class="col-12">
                    <div class="field-error">Passwords do not match.</div>
                  </div>
                }
              </div>

              <hr class="form-divider my-2">

              <!-- ═══ PERSONAL INFO ═══ -->
              <div class="row g-2 mb-2">
                <div class="col-6">
                  <input class="form-control form-control-sm" formControlName="firstName"
                         placeholder="First Name"
                         [class.is-required]="!form.controls.firstName.value?.trim()"
                         [class.is-invalid]="submitted() && form.controls.firstName.invalid" />
                </div>
                <div class="col-6">
                  <input class="form-control form-control-sm" formControlName="lastName"
                         placeholder="Last Name"
                         [class.is-required]="!form.controls.lastName.value?.trim()"
                         [class.is-invalid]="submitted() && form.controls.lastName.invalid" />
                </div>
              </div>
              <div class="row g-2 mb-2">
                <div class="col-7">
                  <input type="email" class="form-control form-control-sm" formControlName="email"
                         placeholder="Email"
                         [class.is-required]="!form.controls.email.value?.trim()"
                         [class.is-invalid]="submitted() && form.controls.email.invalid" />
                </div>
                <div class="col-5">
                  <input type="tel" inputmode="numeric" class="form-control form-control-sm"
                         formControlName="cellphone" (input)="digitsOnly('cellphone', $event)"
                         placeholder="Phone (digits only)"
                         [class.is-required]="!form.controls.cellphone.value?.trim()" />
                </div>
              </div>
              <div class="row g-2 mb-2">
                <div class="col-12">
                  <input class="form-control form-control-sm" formControlName="streetAddress"
                         placeholder="Street Address"
                         [class.is-required]="!form.controls.streetAddress.value?.trim()"
                         [class.is-invalid]="submitted() && form.controls.streetAddress.invalid" />
                </div>
              </div>
              <div class="row g-2 mb-2">
                <div class="col-5">
                  <input class="form-control form-control-sm" formControlName="city"
                         placeholder="City"
                         [class.is-required]="!form.controls.city.value?.trim()"
                         [class.is-invalid]="submitted() && form.controls.city.invalid" />
                </div>
                <div class="col-4">
                  <select class="form-select form-select-sm" formControlName="state"
                          [class.is-required]="!form.controls.state.value"
                          [class.is-invalid]="submitted() && form.controls.state.invalid">
                    <option value="">State</option>
                    @for (s of stateOptions; track s.value) {
                      <option [value]="s.value">{{ s.label }}</option>
                    }
                  </select>
                </div>
                <div class="col-3">
                  <input class="form-control form-control-sm" formControlName="postalCode"
                         placeholder="Zip"
                         [class.is-required]="!form.controls.postalCode.value?.trim()"
                         [class.is-invalid]="submitted() && form.controls.postalCode.invalid" />
                </div>
              </div>

              @if (errorMsg()) {
                <div class="alert alert-danger py-2 small mb-2">{{ errorMsg() }}</div>
              }

              <button type="submit" class="btn btn-primary w-100 fw-semibold mt-3"
                      [disabled]="saving() || !canSubmit()">
                @if (saving()) {
                  <span class="spinner-border spinner-border-sm me-1"></span>Creating...
                } @else {
                  <i class="bi bi-person-plus-fill me-1"></i>Create Account
                }
              </button>
            </form>
          }
        </div>
      </div>
    </tsic-dialog>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClubRepRegisterFormComponent {
    readonly registered = output<{ username: string; password: string }>();
    readonly closed = output<void>();

    private readonly fb = inject(FormBuilder);
    private readonly clubService = inject(ClubService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly stateOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');

    // UI state
    readonly submitted = signal(false);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);
    readonly registrationComplete = signal(false);

    // Club search state
    readonly clubSearchResults = signal<ClubSearchResult[]>([]);
    readonly clubSearchLoading = signal(false);
    readonly clubDecision = signal<ClubDecision>('pending');

    /** Tier 1: 85%+ matches — hard block, show rep contact */
    readonly blockedMatches = computed(() =>
        this.clubSearchResults().filter(c => (c.matchScore ?? 0) >= 85)
    );

    /** Tier 2: 65-84% matches — warning, allow with confirmation */
    readonly warningMatches = computed(() =>
        this.clubSearchResults().filter(c => {
            const score = c.matchScore ?? 0;
            return score >= 65 && score < 85;
        })
    );

    readonly form = this.fb.group({
        clubName: ['', Validators.required],
        firstName: ['', Validators.required],
        lastName: ['', Validators.required],
        email: ['', [Validators.required, Validators.email]],
        cellphone: ['', Validators.required],
        streetAddress: ['', Validators.required],
        city: ['', Validators.required],
        state: ['', Validators.required],
        postalCode: ['', [Validators.required, Validators.pattern(/^\d{5}(-\d{4})?$/)]],
        username: ['', [Validators.required, Validators.minLength(3), Validators.pattern(/^[A-Za-z0-9._-]+$/)]],
        password: ['', [Validators.required, Validators.minLength(6)]],
        confirmPassword: ['', Validators.required],
    });

    /** Cross-field: confirmPassword must match password */
    readonly passwordMismatch = computed(() => {
        const pw = this.form.controls.password.value;
        const cpw = this.form.controls.confirmPassword.value;
        return !!pw && !!cpw && pw !== cpw;
    });

    constructor() {
        // Debounced live search on club name input
        this.form.controls.clubName.valueChanges.pipe(
            debounceTime(300),
            distinctUntilChanged(),
            filter((v): v is string => !!v && v.trim().length >= 3),
            switchMap(name => {
                this.clubSearchLoading.set(true);
                this.clubDecision.set('pending');
                this.clubSearchResults.set([]);
                return this.clubService.searchClubs(name.trim()).pipe(
                    catchError(() => of([] as ClubSearchResult[]))
                );
            }),
            takeUntilDestroyed(this.destroyRef)
        ).subscribe(results => {
            this.clubSearchLoading.set(false);
            this.clubSearchResults.set(results);

            const hasBlocked = results.some(r => (r.matchScore ?? 0) >= 85);
            const hasWarning = results.some(r => {
                const s = r.matchScore ?? 0;
                return s >= 65 && s < 85;
            });

            if (hasBlocked) {
                this.clubDecision.set('blocked');
            } else if (!hasWarning) {
                // No matches at all, or all below 65 — auto-clear
                this.clubDecision.set('clear');
            }
            // else: stays 'pending', user must confirm or abandon
        });

        // Reset when club name gets too short
        this.form.controls.clubName.valueChanges.pipe(
            filter(v => !v || v.trim().length < 3),
            takeUntilDestroyed(this.destroyRef)
        ).subscribe(() => {
            this.clubSearchResults.set([]);
            this.clubSearchLoading.set(false);
            this.clubDecision.set('pending');
        });
    }

    /** Pre-fill a mailto body to make contacting the rep as easy as possible */
    getEmailBody(club: ClubSearchResult): string {
        return `Hi${club.repName ? ' ' + club.repName : ''},\n\n`
             + `I'm trying to register as a rep for ${club.clubName} on TSIC. `
             + `Could you help me get added to the club?\n\n`
             + `Thanks!`;
    }

    encodeURIComponent(value: string): string {
        return encodeURIComponent(value);
    }

    digitsOnly(controlName: string, event: Event): void {
        const input = event.target as HTMLInputElement;
        const digits = input.value.replace(/\D+/g, '').slice(0, 15);
        input.value = digits;
        this.form.get(controlName)?.setValue(digits);
    }

    confirmNewClub(): void {
        this.clubDecision.set('new');
    }

    resetClubDecision(): void {
        this.clubDecision.set('pending');
    }

    /** Submit is allowed when: clear (no matches), or new (confirmed in warning band), and passwords match */
    canSubmit(): boolean {
        const decision = this.clubDecision();
        return (decision === 'clear' || decision === 'new') && !this.passwordMismatch();
    }

    onSubmit(): void {
        this.submitted.set(true);
        if (this.form.invalid || !this.canSubmit() || this.passwordMismatch()) return;

        this.saving.set(true);
        this.errorMsg.set(null);

        const v = this.form.value;
        const request: ClubRepRegistrationRequest = {
            clubName: v.clubName!.trim(),
            firstName: v.firstName!.trim(),
            lastName: v.lastName!.trim(),
            email: v.email!.trim(),
            cellphone: v.cellphone!.trim(),
            streetAddress: v.streetAddress!.trim(),
            city: v.city!.trim(),
            state: v.state!,
            postalCode: v.postalCode!.trim(),
            username: v.username!.trim(),
            password: v.password!,
            confirmedNewClub: true,
        };

        this.clubService.registerClub(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp) => {
                    this.saving.set(false);
                    if (resp.success) {
                        this.registrationComplete.set(true);
                        this.toast.show('Account created! Please sign in.', 'success', 3000);
                        this.registered.emit({ username: request.username, password: request.password });
                    } else if (resp.similarClubs?.length) {
                        // Backend gate caught something the frontend missed
                        this.clubSearchResults.set(resp.similarClubs as ClubSearchResult[]);
                        const hasBlocked = resp.similarClubs.some((c: ClubSearchResult) => (c.matchScore ?? 0) >= 85);
                        this.clubDecision.set(hasBlocked ? 'blocked' : 'pending');
                        this.errorMsg.set(resp.message || null);
                    } else {
                        this.errorMsg.set(resp.message || 'Registration failed.');
                    }
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Request failed.');
                },
            });
    }
}
