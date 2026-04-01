import { ChangeDetectionStrategy, Component, DestroyRef, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { ClubService } from '@infrastructure/services/club.service';
import { FormFieldDataService, type SelectOption } from '@infrastructure/services/form-field-data.service';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { ClubRepRegistrationRequest, ClubSearchResult } from '@core/api';

/**
 * Inline club rep self-registration form.
 * Replaces the "contact your admin" card in the team login step.
 * On success, emits `registered` so the parent can auto-login.
 */
@Component({
    selector: 'app-club-rep-register-form',
    standalone: true,
    imports: [ReactiveFormsModule, TsicDialogComponent],
    template: `
    <div class="register-card">
      @if (registrationComplete()) {
        <!-- Success state -->
        <div class="text-center py-4">
          <i class="bi bi-check-circle-fill text-success" style="font-size: 2.5rem;"></i>
          <h6 class="fw-bold mt-3 mb-2">Account Created!</h6>
          <p class="small text-muted mb-3">Sign in with your new credentials to continue.</p>
        </div>
      } @else if (!expanded()) {
        <!-- Collapsed CTA -->
        <div class="cta-collapsed">
          <i class="bi bi-shield-plus cta-icon"></i>
          <h5 class="fw-bold mb-2">New Club Rep?</h5>
          <p class="small text-muted mb-3">
            Register your club to start adding teams to this event.
          </p>
          <button type="button" class="btn btn-outline-primary btn-sm fw-semibold" (click)="expanded.set(true)">
            <i class="bi bi-person-plus-fill me-1"></i>Create Account
          </button>
        </div>
      } @else {
        <!-- Expanded registration form -->
        <div class="d-flex align-items-center mb-2">
          <h6 class="section-heading mb-0"><i class="bi bi-shield-plus me-1"></i>New Club Rep</h6>
          <button type="button" class="btn btn-link btn-sm ms-auto p-0 text-muted" (click)="expanded.set(false)">
            <i class="bi bi-x-lg"></i>
          </button>
        </div>
        <form [formGroup]="form" (ngSubmit)="onSubmit()">
          <div class="row g-1 mb-1">
            <div class="col-12">
              <input id="cr-club" class="form-control form-control-sm" formControlName="clubName"
                     placeholder="Club Name" [class.is-invalid]="submitted() && form.controls.clubName.invalid" />
            </div>
          </div>
          <div class="row g-1 mb-1">
            <div class="col-6">
              <input id="cr-first" class="form-control form-control-sm" formControlName="firstName"
                     placeholder="First Name" [class.is-invalid]="submitted() && form.controls.firstName.invalid" />
            </div>
            <div class="col-6">
              <input id="cr-last" class="form-control form-control-sm" formControlName="lastName"
                     placeholder="Last Name" [class.is-invalid]="submitted() && form.controls.lastName.invalid" />
            </div>
          </div>
          <div class="row g-1 mb-1">
            <div class="col-7">
              <input id="cr-email" type="email" class="form-control form-control-sm" formControlName="email"
                     placeholder="Email" [class.is-invalid]="submitted() && form.controls.email.invalid" />
            </div>
            <div class="col-5">
              <input id="cr-phone" type="tel" inputmode="numeric" class="form-control form-control-sm"
                     formControlName="cellphone" (input)="digitsOnly('cellphone', $event)"
                     placeholder="Phone (digits)" />
            </div>
          </div>
          <div class="row g-1 mb-1">
            <div class="col-12">
              <input id="cr-addr" class="form-control form-control-sm" formControlName="streetAddress"
                     placeholder="Street Address" [class.is-invalid]="submitted() && form.controls.streetAddress.invalid" />
            </div>
          </div>
          <div class="row g-1 mb-1">
            <div class="col-5">
              <input id="cr-city" class="form-control form-control-sm" formControlName="city"
                     placeholder="City" [class.is-invalid]="submitted() && form.controls.city.invalid" />
            </div>
            <div class="col-4">
              <select id="cr-state" class="form-select form-select-sm" formControlName="state"
                      [class.is-invalid]="submitted() && form.controls.state.invalid">
                <option value="">State</option>
                @for (s of stateOptions; track s.value) {
                  <option [value]="s.value">{{ s.label }}</option>
                }
              </select>
            </div>
            <div class="col-3">
              <input id="cr-zip" class="form-control form-control-sm" formControlName="postalCode"
                     placeholder="Zip" [class.is-invalid]="submitted() && form.controls.postalCode.invalid" />
            </div>
          </div>
          <hr class="form-divider my-1">
          <div class="row g-1 mb-1">
            <div class="col-6">
              <input id="cr-user" class="form-control form-control-sm" formControlName="username"
                     placeholder="Username" autocomplete="username"
                     [class.is-invalid]="submitted() && form.controls.username.invalid" />
            </div>
            <div class="col-6">
              <input id="cr-pass" type="password" class="form-control form-control-sm" formControlName="password"
                     placeholder="Password" autocomplete="new-password"
                     [class.is-invalid]="submitted() && form.controls.password.invalid" />
            </div>
          </div>

          @if (errorMsg()) {
            <div class="alert alert-danger py-1 small mb-1">{{ errorMsg() }}</div>
          }

          <button type="submit" class="btn btn-sm btn-primary w-100 fw-semibold mt-1" [disabled]="saving()">
            @if (saving()) {
              <span class="spinner-border spinner-border-sm me-1"></span>Creating...
            } @else {
              <i class="bi bi-person-plus-fill me-1"></i>Create Account
            }
          </button>
        </form>
      }
    </div>

    <!-- Similar clubs dialog -->
    @if (showSimilarClubs()) {
      <tsic-dialog [open]="true" size="sm" (requestClose)="dismissSimilarClubs()">
        <div class="modal-content">
          <div class="modal-header">
            <h5 class="modal-title"><i class="bi bi-search me-2"></i>Similar Clubs Found</h5>
            <button type="button" class="btn-close" (click)="dismissSimilarClubs()" aria-label="Close"></button>
          </div>
          <div class="modal-body">
            <p class="small text-muted mb-3">
              We found clubs with similar names. Is one of these yours?
            </p>
            @for (club of similarClubs(); track club.clubId) {
              <button type="button" class="btn btn-outline-primary btn-sm w-100 mb-2 text-start"
                      (click)="chooseSimilarClub(club)">
                <div class="d-flex justify-content-between align-items-center">
                  <span class="fw-medium">{{ club.clubName }}</span>
                  <span class="badge bg-primary-subtle text-primary-emphasis">{{ club.matchScore }}% match</span>
                </div>
                @if (club.state) {
                  <div class="small text-muted">{{ club.state }} &bull; {{ club.teamCount }} teams</div>
                }
              </button>
            }
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="dismissSimilarClubs()">
              None of these — keep my new club
            </button>
          </div>
        </div>
      </tsic-dialog>
    }
  `,
    styles: [`
      .register-card {
        height: 100%;
        padding: var(--space-3);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-lg);
        background: var(--brand-surface);
        box-shadow: var(--shadow-sm);
      }

      .cta-collapsed {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        text-align: center;
        height: 100%;
        padding: var(--space-6) var(--space-4);
      }

      .cta-icon {
        font-size: 2.5rem;
        color: var(--bs-primary);
        margin-bottom: var(--space-3);
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClubRepRegisterFormComponent {
    readonly registered = output<{ username: string; password: string }>();

    private readonly fb = inject(FormBuilder);
    private readonly clubService = inject(ClubService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly stateOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');

    readonly expanded = signal(false);
    readonly submitted = signal(false);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);
    readonly registrationComplete = signal(false);
    readonly showSimilarClubs = signal(false);
    readonly similarClubs = signal<ClubSearchResult[]>([]);

    private savedCredentials: { username: string; password: string } | null = null;

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
    });

    digitsOnly(controlName: string, event: Event): void {
        const input = event.target as HTMLInputElement;
        const digits = input.value.replace(/\D+/g, '').slice(0, 15);
        input.value = digits;
        this.form.get(controlName)?.setValue(digits);
    }

    onSubmit(): void {
        this.submitted.set(true);
        if (this.form.invalid) return;

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
        };

        this.clubService.registerClub(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp) => {
                    this.saving.set(false);
                    if (resp.success) {
                        this.savedCredentials = { username: request.username, password: request.password };

                        // Check for similar clubs
                        if (resp.similarClubs?.length) {
                            this.similarClubs.set(resp.similarClubs as ClubSearchResult[]);
                            this.showSimilarClubs.set(true);
                        } else {
                            this.completeRegistration();
                        }
                    } else {
                        this.errorMsg.set(resp.message || 'Registration failed.');
                    }
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { status?: number; error?: { message?: string; similarClubs?: ClubSearchResult[] } };

                    // 409 = similar clubs found but registration still succeeded
                    if (httpErr.status === 409 && httpErr.error?.similarClubs?.length) {
                        this.savedCredentials = { username: request.username, password: request.password };
                        this.similarClubs.set(httpErr.error.similarClubs);
                        this.showSimilarClubs.set(true);
                        return;
                    }

                    this.errorMsg.set(httpErr?.error?.message || 'Request failed.');
                },
            });
    }

    chooseSimilarClub(club: ClubSearchResult): void {
        this.showSimilarClubs.set(false);
        // TODO: call addClub with UseExistingClubId if needed
        // For now, just complete — the club was already created
        this.toast.show(`Note: "${club.clubName}" already exists. You may want to contact your league admin to merge.`, 'info', 5000);
        this.completeRegistration();
    }

    dismissSimilarClubs(): void {
        this.showSimilarClubs.set(false);
        this.completeRegistration();
    }

    private completeRegistration(): void {
        this.registrationComplete.set(true);
        this.toast.show('Club rep account created! Please sign in.', 'success', 3000);
        if (this.savedCredentials) {
            this.registered.emit(this.savedCredentials);
        }
    }
}
