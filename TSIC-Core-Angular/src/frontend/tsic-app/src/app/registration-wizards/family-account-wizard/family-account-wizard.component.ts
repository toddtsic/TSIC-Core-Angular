import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FamilyAccountWizardService } from './family-account-wizard.service';
import { FamAccountStepAccountComponent } from './steps/account-info.component';
import { FamAccountStepChildrenComponent } from './steps/add-children.component';
import { FamAccountStepReviewComponent } from './steps/review.component';
import { FamAccountStepAddressComponent } from './steps/address-info.component';
import { FamAccountStepCredentialsComponent } from './steps/credentials.component';
import { JobService } from '../../core/services/job.service';
import { WizardThemeDirective } from '../../shared/directives/wizard-theme.directive';
import { FamilyService } from '../../core/services/family.service';
import { AuthService } from '../../core/services/auth.service';

@Component({
    selector: 'app-family-account-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, MatButtonModule, WizardThemeDirective, FamAccountStepCredentialsComponent, FamAccountStepAccountComponent, FamAccountStepAddressComponent, FamAccountStepChildrenComponent, FamAccountStepReviewComponent],
    templateUrl: './family-account-wizard.component.html',
    styleUrls: ['./family-account-wizard.component.scss'],
    host: {}
})
export class FamilyAccountWizardComponent implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    readonly state = inject(FamilyAccountWizardService);
    private readonly jobService = inject(JobService);
    private readonly familyService = inject(FamilyService);
    private readonly auth = inject(AuthService);

    currentStep = signal(1);
    totalSteps = computed(() => this.state.mode() === 'create' ? 5 : 4);
    progressPercent = computed(() => Math.round((this.currentStep() / this.totalSteps()) * 100));
    // Step labels to mirror Player Registration styling
    private readonly labelsCreate = ['Credentials', 'Contacts', 'Address', 'Children', 'Review'];
    private readonly labelsEdit = ['Contacts', 'Address', 'Children', 'Review'];
    stepLabels = computed(() => this.state.mode() === 'create' ? this.labelsCreate : this.labelsEdit);
    currentIndex = computed(() => Math.max(0, Math.min(this.currentStep() - 1, this.stepLabels().length - 1)));

    private readonly returnUrl = signal<string | null>(null);
    private nextAction: 'register-player' | null = null;

    ngOnInit(): void {
        this.returnUrl.set(this.route.snapshot.queryParamMap.get('returnUrl'));
        const nextParam = this.route.snapshot.queryParamMap.get('next');
        if (nextParam === 'register-player') this.nextAction = 'register-player';
        const mode = this.route.snapshot.queryParamMap.get('mode');
        if (mode === 'edit') {
            this.state.mode.set('edit');
        }

        // If a returnUrl carries a jobPath, pre-load the job to get dynamic labels
        const ru = this.returnUrl();
        const jobPath = this.extractJobPathFromReturnUrl(ru);
        if (jobPath) {
            this.jobService.loadJobMetadata(jobPath);
        }

        // If we're editing and unauthenticated, send user to login and return here afterward
        if (this.state.mode() === 'edit' && !this.auth.isAuthenticated()) {
            const currentUrl = this.router.url;
            this.router.navigate(['/tsic/login'], {
                queryParams: {
                    returnUrl: currentUrl,
                    theme: 'family',
                    header: 'Family Account Login',
                    subHeader: 'Sign in to continue'
                }
            });
            return;
        }

        // If we're editing and the user is authenticated, load current family profile to populate the wizard
        if (this.state.mode() === 'edit' && this.auth.isAuthenticated()) {
            this.familyService.getMyFamily().subscribe({
                next: (p) => {
                    // Populate contacts
                    this.state.username.set(p.username ?? '');
                    this.state.parent1FirstName.set(p.primary?.firstName ?? '');
                    this.state.parent1LastName.set(p.primary?.lastName ?? '');
                    this.state.parent1Phone.set(p.primary?.cellphone ?? '');
                    this.state.parent1Email.set(p.primary?.email ?? '');
                    this.state.parent1EmailConfirm.set(p.primary?.email ?? '');

                    this.state.parent2FirstName.set(p.secondary?.firstName ?? '');
                    this.state.parent2LastName.set(p.secondary?.lastName ?? '');
                    this.state.parent2Phone.set(p.secondary?.cellphone ?? '');
                    this.state.parent2Email.set(p.secondary?.email ?? '');
                    this.state.parent2EmailConfirm.set(p.secondary?.email ?? '');

                    // Address
                    this.state.address1.set(p.address?.streetAddress ?? '');
                    this.state.city.set(p.address?.city ?? '');
                    this.state.state.set(p.address?.state ?? '');
                    this.state.postalCode.set(p.address?.postalCode ?? '');

                    // Children
                    const kids = (p.children ?? []).map(c => ({
                        firstName: c.firstName ?? '',
                        lastName: c.lastName ?? '',
                        gender: c.gender ?? '',
                        dob: c.dob ?? undefined,
                        email: c.email ?? undefined,
                        phone: c.phone ?? undefined
                    }));
                    this.state.children.set(kids);
                }
            });
        }
    }

    next(): void {
        if (this.currentStep() < this.totalSteps()) {
            this.currentStep.update(s => s + 1);
        }
    }

    back(): void {
        this.currentStep.update(s => Math.max(1, s - 1));
    }

    finish(action?: 'home' | 'register'): void {
        const ru = this.returnUrl();
        // If a safe returnUrl was provided and no explicit "register" action overrides it, honor it first
        if (ru && action !== 'register' && this.nextAction !== 'register-player') {
            try {
                const u = new URL(ru, globalThis.location.origin);
                const internalPath = `${u.pathname}${u.search}${u.hash}`;
                this.router.navigateByUrl(internalPath);
                return;
            } catch {
                // fall through to jobPath-based routing
            }
        }

        // Prefer known job from JobService; fall back to parsing returnUrl if provided
        const jobPath = this.jobService.getCurrentJob()?.jobPath || this.extractJobPathFromReturnUrl(ru);
        if (!jobPath) {
            this.router.navigateByUrl('/tsic/role-selection');
            return;
        }
        // If nextAction is set to register-player, prefer that unless user explicitly chose 'home'
        if (this.nextAction === 'register-player' && action !== 'home') {
            this.router.navigateByUrl(`/${jobPath}/register-player`);
        } else if (action === 'register') {
            this.router.navigateByUrl(`/${jobPath}/register-player`);
        } else {
            this.router.navigateByUrl(`/${jobPath}`);
        }
    }

    private extractJobPathFromReturnUrl(ru: string | null): string | null {
        if (!ru) return null;
        try {
            const u = new URL(ru, globalThis.location.origin);
            const first = u.pathname.split('/').find(Boolean);
            return first ?? null;
        } catch {
            return null;
        }
    }
}
