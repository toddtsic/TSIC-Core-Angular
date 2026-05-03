import { AfterViewInit, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, input, OnInit, output, signal, computed, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { debounceTime, distinctUntilChanged, filter, switchMap, catchError, tap } from 'rxjs/operators';
import { of } from 'rxjs';
import { ClubService } from '@infrastructure/services/club.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { TosContentComponent } from '../../shared/components/tos-content.component';
import { FormFieldDataService, type SelectOption } from '@infrastructure/services/form-field-data.service';
import { ToastService } from '@shared-ui/toast.service';
import type { ClubRepRegistrationRequest, ClubRepProfileDto, ClubRepProfileUpdateRequest, ClubSearchResult } from '@core/api';

/**
 * Club decision state:
 * - 'pending' — matches found, user hasn't confirmed "create new" yet
 * - 'new'     — user confirmed "create new" against the matches
 * - 'clear'   — no matches found, auto-approved for new club
 */
type ClubDecision = 'pending' | 'new' | 'clear';

/**
 * Club rep self-registration / profile-edit form with single-tier similarity gate.
 * Renders form fields only — the consumer owns the title and card chrome.
 *
 * 65%+ match — surfaces existing clubs and asks the rep to either contact
 *   the existing rep (if one is theirs) or confirm "create new" (e.g. a
 *   regional chapter of a national org legitimately registering as a sibling).
 *
 * Below 65% — no friction, new club created automatically.
 */
@Component({
    selector: 'app-club-rep-register-form',
    standalone: true,
    imports: [ReactiveFormsModule, TosContentComponent],
    styles: [`
      :host { display: block; }

      /* Hero-sized variant of .field-input — used for the Club Name input,
         which is the primary action target of the create form. */
      .field-input--hero {
        padding: var(--space-2) var(--space-3);
        font-size: var(--font-size-base);
        line-height: 1.4;
      }

      /* ── Similar-clubs panel (single tier, 65%+) ────────────────────────
         Mode-adaptive: panel surface inherits from .card-body (white in light,
         dark in dark). Text uses --brand-text. The yellow/red tints are rgba
         overlays that work over either surface. Do NOT force a palette-locked
         background here — the dark theme overrides .card-body with !important
         and a locked-white panel breaks the cascade for descendant text.
         See feedback_neutral0_text_color_trap.md.
      */
      .club-similar-panel {
        border: 2px solid var(--bs-warning);
        border-radius: var(--radius-md);
        margin-top: var(--space-2);
        overflow: hidden;
      }
      /* Hard-block modifier: exact-normalized duplicate. Cannot be created. */
      .club-similar-panel--exact { border-color: var(--bs-danger); }
      .club-similar-panel--exact .club-similar-header {
        background: rgba(var(--bs-danger-rgb), 0.18);
        border-bottom-color: rgba(var(--bs-danger-rgb), 0.3);
      }
      .club-similar-header {
        padding: var(--space-3);
        background: rgba(var(--bs-warning-rgb), 0.18);
        border-bottom: 1px solid rgba(var(--bs-warning-rgb), 0.25);
      }
      .club-similar-header h6 {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
        margin: 0 0 var(--space-2);
      }
      .club-similar-header p {
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        margin: var(--space-2) 0 0;
        line-height: var(--line-height-normal);
      }
      .similar-club-row {
        padding: var(--space-3);
        border-bottom: 1px solid var(--border-color);
      }
      .similar-club-row:last-child { border-bottom: none; }
      .rep-contact {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        margin-top: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-primary-rgb), 0.08);
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
      .similar-confirm-bar {
        padding: var(--space-3);
        background: rgba(var(--bs-warning-rgb), 0.06);
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
      .form-section-title {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        margin: 0 0 var(--space-2);
        display: flex;
        align-items: center;
      }
      .form-section-title i { color: var(--bs-primary); }
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
      .confidence-exact {
        background: var(--bs-danger);
        color: var(--neutral-0);
      }
      .confidence-high {
        background: rgba(var(--bs-danger-rgb), 0.15);
        color: var(--bs-danger);
      }
      .confidence-medium {
        background: rgba(var(--bs-warning-rgb), 0.18);
        color: var(--bs-warning);
      }
      .mega-club-tag {
        font-size: var(--font-size-xs);
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-info-rgb), 0.1);
        color: var(--bs-info);
      }

      /* ── ToS acceptance row (above Create Account) ─────── */
      .tos-acceptance-row {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        margin-top: var(--space-2);
        background: var(--brand-bg);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        line-height: 1.4;
      }
      .tos-acceptance-row input[type="checkbox"] {
        flex-shrink: 0;
        width: 18px;
        height: 18px;
        margin-top: 1px;
        accent-color: var(--bs-primary);
        cursor: pointer;
      }
      .tos-acceptance-row label {
        margin: 0;
        cursor: pointer;
        user-select: none;
      }
      .tos-link-btn {
        background: none;
        border: none;
        padding: 0;
        color: var(--bs-primary);
        font: inherit;
        text-decoration: underline;
        cursor: pointer;
      }
      .tos-link-btn:hover { text-decoration: none; }
      .tos-link-btn:focus-visible {
        outline: none;
        box-shadow: var(--shadow-focus);
        border-radius: var(--radius-sm);
      }

      /* ── Inline collapsible ToS panel ─────────────────── */
      .tos-inline-panel {
        margin-top: var(--space-2);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        overflow: hidden;
      }
      .tos-inline-scroll {
        max-height: 320px;
        overflow-y: auto;
        padding: var(--space-3) var(--space-4);
      }
    `],
    template: `
    <form [formGroup]="form" (ngSubmit)="onSubmit()">

              @if (!isEdit()) {
              <!-- ═══ CLUB NAME INPUT (hero of the form) ═══ -->
              <div class="mb-2">
                <label class="field-label">Club Name <span class="req-star">*</span></label>
                <input #clubNameInput class="field-input field-input--hero" formControlName="clubName"
                       placeholder="Start typing your club name..."
                       autocomplete="off"
                       [class.is-invalid]="submitted() && form.controls.clubName.invalid" />
                <div class="value-prop mt-1">
                  <i class="bi bi-lightning-charge me-1"></i>
                  Your TeamSportsInfo <strong>Club Team Library</strong> is permanent. Add each team once, register from the list at every future event.
                </div>
              </div>

              <!-- Loading -->
              @if (clubSearchLoading()) {
                <div class="text-center py-2">
                  <span class="spinner-border spinner-border-sm text-primary me-1"></span>
                  <span class="small text-muted">Checking for your club...</span>
                </div>
              }

              <!-- ═══ SIMILAR / EXACT MATCH PANEL ═══
                   Exact-normalized match → hard block (no Create-new button, danger framing).
                   Otherwise → similarity surface with Create-new available. -->
              @if (similarMatches().length > 0 && !clubSearchLoading() && clubDecision() !== 'new') {
                <div class="club-similar-panel" [class.club-similar-panel--exact]="exactMatch() !== null">
                  <div class="club-similar-header">
                    @if (exactMatch(); as exact) {
                      <h6><i class="bi bi-shield-exclamation me-2 text-danger"></i>This club is already registered</h6>
                      <p>
                        <strong>"{{ exact.clubName }}" already has a registered rep</strong> —
                        that rep is responsible for registering teams under this club. Creating a
                        duplicate account would bypass them and split team history.
                      </p>
                      <p>
                        If you have a team that should play for this club, contact the existing
                        rep below — they handle the registration.
                      </p>
                      <p>
                        <strong>If you're a different regional chapter</strong> of a national
                        organization, register with a name that distinguishes your region
                        (e.g. "Aacme Lacrosse NJ").
                      </p>
                    } @else {
                      <h6><i class="bi bi-exclamation-triangle me-2 text-warning"></i>Similar clubs already on file</h6>
                      <p>
                        <strong>If one of these IS your club</strong>, please contact that rep below
                        instead of creating a duplicate — duplicate clubs can't share team history.
                      </p>
                      <p>
                        <strong>If you're a different regional chapter</strong> of a national organization
                        (e.g. "Aacme Lax NJ" vs "Aacme Lax MA"), that's expected — each chapter has its
                        own account so families and tournament directors reach the right rep. Go ahead
                        and create yours.
                      </p>
                      <p>
                        <strong>National-org reps:</strong> please don't register on behalf of local
                        chapters. Local reps need to own their teams and contact info directly.
                      </p>
                    }
                  </div>
                  @for (club of displayedMatches(); track club.clubId) {
                    <div class="similar-club-row">
                      <div class="d-flex justify-content-between align-items-center">
                        <div>
                          <span class="fw-semibold">{{ club.clubName }}</span>
                          @if (club.state) {
                            <span class="text-muted ms-1 small">({{ club.state }})</span>
                          }
                          @if (club.teamCount) {
                            <span class="text-muted small ms-1">&bull; {{ club.teamCount }} teams</span>
                          }
                        </div>
                        <div class="d-flex align-items-center gap-1">
                          @if (club.isRelatedClub) {
                            <span class="mega-club-tag"><i class="bi bi-diagram-3 me-1"></i>Same org</span>
                          }
                          @if (club.isExactMatch) {
                            <span class="match-confidence confidence-exact">Already registered</span>
                          } @else if (club.matchScore >= 85) {
                            <span class="match-confidence confidence-high">Very similar</span>
                          } @else {
                            <span class="match-confidence confidence-medium">Similar</span>
                          }
                        </div>
                      </div>
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
                  @if (exactMatch() === null) {
                    <div class="similar-confirm-bar">
                      <button type="button" class="btn btn-sm btn-outline-primary fw-medium"
                              (click)="confirmNewClub()">
                        <i class="bi bi-plus-circle me-1"></i>None of these are my club — create new
                      </button>
                      <div class="small text-muted mt-1">This starts a new account for your club.</div>
                    </div>
                  }
                </div>
              }

              <!-- ═══ NEW CLUB CONFIRMED ═══ -->
              @if (clubDecision() === 'new' && similarMatches().length > 0) {
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
              }

              @if (isEdit() || clubDecision() === 'clear' || clubDecision() === 'new') {
                <hr class="form-divider my-3">
                <h6 class="form-section-title">
                  <i class="bi bi-person-vcard me-2"></i>Club Rep Details
                </h6>

                @if (!isEdit()) {
                  <!-- ═══ CREDENTIALS ═══ -->
                  <div class="row g-2 mb-2">
                    <div class="col-12">
                      <input class="field-input" formControlName="username"
                             placeholder="Username" autocomplete="username"
                             [class.is-required]="!form.controls.username.value?.trim()"
                             [class.is-invalid]="submitted() && form.controls.username.invalid" />
                    </div>
                    <div class="col-6">
                      <div class="position-relative">
                        <input [type]="showPassword() ? 'text' : 'password'" class="field-input pe-5" formControlName="password"
                               placeholder="Password" autocomplete="new-password"
                               [class.is-required]="!form.controls.password.value"
                               [class.is-invalid]="submitted() && form.controls.password.invalid" />
                        <button type="button" class="password-toggle"
                                (click)="showPassword.set(!showPassword())"
                                [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'" tabindex="-1">
                          <i class="bi" [class.bi-eye]="!showPassword()" [class.bi-eye-slash]="showPassword()"></i>
                        </button>
                      </div>
                    </div>
                    <div class="col-6">
                      <div class="position-relative">
                        <input [type]="showConfirm() ? 'text' : 'password'" class="field-input pe-5" formControlName="confirmPassword"
                               placeholder="Confirm Password" autocomplete="new-password"
                               [class.is-invalid]="submitted() && passwordMismatch()" />
                        <button type="button" class="password-toggle"
                                (click)="showConfirm.set(!showConfirm())"
                                [attr.aria-label]="showConfirm() ? 'Hide password' : 'Show password'" tabindex="-1">
                          <i class="bi" [class.bi-eye]="!showConfirm()" [class.bi-eye-slash]="showConfirm()"></i>
                        </button>
                      </div>
                    </div>
                    @if (submitted() && passwordMismatch()) {
                      <div class="col-12">
                        <div class="field-error">Passwords do not match.</div>
                      </div>
                    }
                  </div>

                  <hr class="form-divider my-2">
                }

                <!-- ═══ PERSONAL INFO ═══ -->
                <div class="row g-2 mb-2">
                  <div class="col-6">
                    <input #firstNameInput class="field-input" formControlName="firstName"
                           placeholder="First Name"
                           [class.is-required]="!form.controls.firstName.value?.trim()"
                           [class.is-invalid]="submitted() && form.controls.firstName.invalid" />
                  </div>
                  <div class="col-6">
                    <input class="field-input" formControlName="lastName"
                           placeholder="Last Name"
                           [class.is-required]="!form.controls.lastName.value?.trim()"
                           [class.is-invalid]="submitted() && form.controls.lastName.invalid" />
                  </div>
                </div>
                <div class="row g-2 mb-2">
                  <div class="col-7">
                    <input type="email" class="field-input" formControlName="email"
                           placeholder="Email"
                           [class.is-required]="!form.controls.email.value?.trim()"
                           [class.is-invalid]="submitted() && form.controls.email.invalid" />
                  </div>
                  <div class="col-5">
                    <input type="tel" inputmode="numeric" class="field-input"
                           formControlName="cellphone" (input)="digitsOnly('cellphone', $event)"
                           placeholder="Phone (digits only)"
                           [class.is-required]="!form.controls.cellphone.value?.trim()" />
                  </div>
                </div>
                <div class="row g-2 mb-2">
                  <div class="col-12">
                    <input class="field-input" formControlName="streetAddress"
                           autocomplete="address-line1"
                           placeholder="Street Address"
                           [class.is-required]="!form.controls.streetAddress.value?.trim()"
                           [class.is-invalid]="submitted() && form.controls.streetAddress.invalid" />
                  </div>
                </div>
                <div class="row g-2 mb-2">
                  <div class="col-5">
                    <input class="field-input" formControlName="city"
                           autocomplete="address-level2"
                           placeholder="City"
                           [class.is-required]="!form.controls.city.value?.trim()"
                           [class.is-invalid]="submitted() && form.controls.city.invalid" />
                  </div>
                  <div class="col-4">
                    <select class="field-select" formControlName="state"
                            autocomplete="address-level1"
                            [class.is-required]="!form.controls.state.value"
                            [class.is-invalid]="submitted() && form.controls.state.invalid">
                      <option value="">State</option>
                      @for (s of stateOptions; track s.value) {
                        <option [value]="s.value">{{ s.label }}</option>
                      }
                    </select>
                  </div>
                  <div class="col-3">
                    <input class="field-input" formControlName="postalCode"
                           autocomplete="postal-code"
                           placeholder="Zip"
                           [class.is-required]="!form.controls.postalCode.value?.trim()"
                           [class.is-invalid]="submitted() && form.controls.postalCode.invalid" />
                  </div>
                </div>

                @if (errorMsg()) {
                  <div class="alert alert-danger py-2 small mb-2">{{ errorMsg() }}</div>
                }

                @if (!isEdit()) {
                  <div class="tos-acceptance-row">
                    <input id="clubRepTosAccept" type="checkbox" formControlName="agreeToTos"
                           [class.is-invalid]="submitted() && form.controls.agreeToTos.invalid" />
                    <label for="clubRepTosAccept">
                      I have read and agree to the
                      <button type="button" class="tos-link-btn"
                              [attr.aria-expanded]="tosExpanded()"
                              aria-controls="clubRepTosPanel"
                              (click)="tosExpanded.set(!tosExpanded())">
                        Terms of Service<i class="bi ms-1"
                          [class.bi-chevron-down]="!tosExpanded()"
                          [class.bi-chevron-up]="tosExpanded()"></i>
                      </button>.
                    </label>
                  </div>
                  @if (tosExpanded()) {
                    <div id="clubRepTosPanel" class="tos-inline-panel">
                      <div class="tos-inline-scroll">
                        <app-tos-content />
                      </div>
                    </div>
                  }
                }

                <button type="submit" class="btn btn-primary w-100 fw-semibold mt-3"
                        [disabled]="saving() || (!isEdit() && !canSubmit())">
                  @if (saving()) {
                    <span class="spinner-border spinner-border-sm me-1"></span>
                    @if (isEdit()) { Saving... } @else { Creating... }
                  } @else {
                    @if (isEdit()) {
                      <i class="bi bi-check-lg me-1"></i>Save Changes
                    } @else {
                      <i class="bi bi-person-plus-fill me-1"></i>Create Account
                    }
                  }
                </button>
              }
      </form>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClubRepRegisterFormComponent implements OnInit, AfterViewInit {
    /** 'create' = self-register a new ClubRep (default). 'edit' = update current profile. */
    readonly mode = input<'create' | 'edit'>('create');
    /** Existing profile data to prefill in edit mode. */
    readonly existing = input<ClubRepProfileDto | null>(null);

    readonly registered = output<{ username: string; password: string }>();
    readonly saved = output<void>();

    readonly isEdit = computed(() => this.mode() === 'edit');

    private readonly fb = inject(FormBuilder);
    private readonly clubService = inject(ClubService);
    private readonly auth = inject(AuthService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    // First input refs — focused on view init to drop the user straight into the form.
    private readonly clubNameInput = viewChild<ElementRef<HTMLInputElement>>('clubNameInput');
    private readonly firstNameInput = viewChild<ElementRef<HTMLInputElement>>('firstNameInput');

    readonly stateOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');

    // UI state
    readonly submitted = signal(false);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);
    readonly showPassword = signal(false);
    readonly showConfirm = signal(false);
    readonly tosExpanded = signal(false);

    // Club search state
    readonly clubSearchResults = signal<ClubSearchResult[]>([]);
    readonly clubSearchLoading = signal(false);
    readonly clubDecision = signal<ClubDecision>('pending');

    /** All 65%+ matches — surfaced in a single panel; "Very similar" pill on 85%+ */
    readonly similarMatches = computed(() =>
        this.clubSearchResults().filter(c => c.matchScore >= 65)
    );

    /** First exact-normalized match if any. When present, registration is hard-blocked. */
    readonly exactMatch = computed(() =>
        this.clubSearchResults().find(c => c.isExactMatch) ?? null
    );

    /** Rows to render in the panel: only the exact match when blocked (the other
     *  similars are noise once the user can't proceed); otherwise all 65%+ matches. */
    readonly displayedMatches = computed(() => {
        const exact = this.exactMatch();
        return exact ? [exact] : this.similarMatches();
    });

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
        agreeToTos: [false, Validators.requiredTrue],
    });

    /** Cross-field: confirmPassword must match password */
    readonly passwordMismatch = computed(() => {
        const pw = this.form.controls.password.value;
        const cpw = this.form.controls.confirmPassword.value;
        return !!pw && !!cpw && pw !== cpw;
    });

    constructor() {
        // Live search — keep prior results visible while typing to avoid stutter.
        // Only clear when the input drops below the search threshold.
        this.form.controls.clubName.valueChanges.pipe(
            distinctUntilChanged(),
            tap((v) => {
                if (!v || v.trim().length < 3) {
                    this.clubSearchResults.set([]);
                    this.clubDecision.set('pending');
                    this.clubSearchLoading.set(false);
                }
            }),
            debounceTime(300),
            filter((v): v is string => !!v && v.trim().length >= 3),
            tap(() => this.clubSearchLoading.set(true)),
            switchMap(name => this.clubService.searchClubs(name.trim()).pipe(
                catchError(() => of([] as ClubSearchResult[]))
            )),
            takeUntilDestroyed(this.destroyRef)
        ).subscribe(results => {
            this.clubSearchLoading.set(false);
            this.clubSearchResults.set(results);

            const hasSimilar = results.some(r => r.matchScore >= 65);
            this.clubDecision.set(hasSimilar ? 'pending' : 'clear');
        });
    }

    ngOnInit(): void {
        if (!this.isEdit()) return;

        // Edit mode: disable the creation-only fields (they're excluded from form.value
        // and from form.invalid when disabled) and prefill the profile fields.
        this.form.controls.clubName.disable();
        this.form.controls.username.disable();
        this.form.controls.password.disable();
        this.form.controls.confirmPassword.disable();
        this.form.controls.agreeToTos.disable();
        // Identity fields are locked too: free name edits would let one rep "hand off"
        // an account by renaming it instead of creating a proper new club rep account.
        this.form.controls.firstName.disable();
        this.form.controls.lastName.disable();

        // canSubmit() gates on clubDecision; 'clear' skips the club-search gate entirely.
        this.clubDecision.set('clear');

        const data = this.existing();
        if (data) {
            this.form.patchValue({
                firstName: data.firstName,
                lastName: data.lastName,
                email: data.email,
                cellphone: data.cellphone,
                streetAddress: data.streetAddress,
                city: data.city,
                state: data.state,
                postalCode: data.postalCode,
            });
        }
    }

    ngAfterViewInit(): void {
        // Drop the user straight into the form: clubName for create, firstName for edit.
        // setTimeout defers past the change-detection tick so the @if branch is in the DOM.
        setTimeout(() => {
            const target = this.isEdit() ? this.firstNameInput() : this.clubNameInput();
            target?.nativeElement.focus();
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

    /** Submit is allowed when: clear (no matches), or new (confirmed against similar matches), passwords match, and ToS accepted.
     *  Exact-normalized match always blocks (cannot be bypassed — backend agrees). */
    canSubmit(): boolean {
        if (this.exactMatch() !== null) return false;
        const decision = this.clubDecision();
        return (decision === 'clear' || decision === 'new')
            && !this.passwordMismatch()
            && this.form.controls.agreeToTos.value === true;
    }

    onSubmit(): void {
        this.submitted.set(true);
        if (this.isEdit()) {
            if (this.form.invalid) return;
            this.submitEdit();
            return;
        }

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
            acceptedTos: true,
        };

        this.clubService.registerClub(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp) => {
                    if (resp.success) {
                        // Backend has already stamped bTSICWaiverSigned + timestamp.
                        // Auto-login and emit registered — no separate ToS step.
                        this.autoLoginAndEmit(request.username, request.password);
                    } else if (resp.similarClubs?.length) {
                        this.saving.set(false);
                        // Backend gate caught something the frontend missed
                        this.clubSearchResults.set(resp.similarClubs as ClubSearchResult[]);
                        this.clubDecision.set('pending');
                        this.errorMsg.set(resp.message || null);
                    } else {
                        this.saving.set(false);
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

    /** After successful registration: sign the user in and emit `registered`. */
    private autoLoginAndEmit(username: string, password: string): void {
        this.auth.login({ username, password })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.registered.emit({ username, password });
                },
                error: () => {
                    this.saving.set(false);
                    this.errorMsg.set('Account created, but sign-in failed. Please log in manually.');
                },
            });
    }

    private submitEdit(): void {
        this.saving.set(true);
        this.errorMsg.set(null);

        const v = this.form.getRawValue();
        const request: ClubRepProfileUpdateRequest = {
            firstName: (v.firstName ?? '').trim(),
            lastName: (v.lastName ?? '').trim(),
            email: (v.email ?? '').trim(),
            cellphone: (v.cellphone ?? '').trim(),
            streetAddress: (v.streetAddress ?? '').trim(),
            city: (v.city ?? '').trim(),
            state: v.state ?? '',
            postalCode: (v.postalCode ?? '').trim(),
        };

        this.clubService.updateSelfProfile(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.toast.show('Profile updated.', 'success', 2500);
                    this.saved.emit();
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Update failed.');
                },
            });
    }

}
