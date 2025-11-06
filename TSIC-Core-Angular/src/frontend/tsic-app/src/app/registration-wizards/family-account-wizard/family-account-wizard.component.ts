import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FamilyAccountWizardService } from './family-account-wizard.service';
import { FamAccountStepAccountComponent } from './steps/account-info.component';
import { FamAccountStepChildrenComponent } from './steps/add-children.component';
import { FamAccountStepReviewComponent } from './steps/review.component';
import { FamAccountStepAddressComponent } from './steps/address-info.component';
import { FamAccountStepCredentialsComponent } from './steps/credentials.component';
import { JobService } from '../../core/services/job.service';

@Component({
    selector: 'app-family-account-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, FamAccountStepCredentialsComponent, FamAccountStepAccountComponent, FamAccountStepAddressComponent, FamAccountStepChildrenComponent, FamAccountStepReviewComponent],
    templateUrl: './family-account-wizard.component.html',
    styleUrls: ['./family-account-wizard.component.scss']
})
export class FamilyAccountWizardComponent implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    readonly state = inject(FamilyAccountWizardService);
    private readonly jobService = inject(JobService);

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
