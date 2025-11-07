import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FamilyAccountWizardService } from './family-account-wizard.service';
import { FamAccountStepAccountComponent } from './steps/account-info.component';
import { FamAccountStepChildrenComponent } from './steps/add-children.component';
import { FamAccountStepReviewComponent } from './steps/review.component';
import { WizardThemeDirective } from '../shared/directives/wizard-theme.directive';

@Component({
    selector: 'app-family-account-wizard',
    standalone: true,
    imports: [CommonModule, RouterModule, WizardThemeDirective, FamAccountStepAccountComponent, FamAccountStepChildrenComponent, FamAccountStepReviewComponent],
    templateUrl: './family-account-wizard.component.html',
    styleUrls: ['./family-account-wizard.component.scss']
})
export class FamilyAccountWizardComponent implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    readonly state = inject(FamilyAccountWizardService);

    currentStep = signal(1);
    totalSteps = signal(3);
    progressPercent = computed(() => Math.round((this.currentStep() / this.totalSteps()) * 100));

    private readonly returnUrl = signal<string | null>(null);

    ngOnInit(): void {
        this.returnUrl.set(this.route.snapshot.queryParamMap.get('returnUrl'));
    }

    next(): void {
        if (this.currentStep() < this.totalSteps()) {
            this.currentStep.update(s => s + 1);
        }
    }

    back(): void {
        this.currentStep.update(s => Math.max(1, s - 1));
    }

    finish(): void {
        const dest = this.returnUrl() || '/tsic/role-selection';
        this.router.navigateByUrl(dest);
    }
}
