import { TestBed } from '@angular/core/testing';
import { TeamRegistrationWizardComponent } from './team-registration-wizard.component';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

describe('TeamRegistrationWizardComponent', () => {
    beforeEach(() => {
        TestBed.configureTestingModule({
            imports: [TeamRegistrationWizardComponent],
            providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
        });
    });

    it('should start at step 1 (Login)', () => {
        const fixture = TestBed.createComponent(TeamRegistrationWizardComponent);
        const component = fixture.componentInstance;
        expect(component.step()).toBe(1);
    });

    it('should go back one step with prevStep', () => {
        const fixture = TestBed.createComponent(TeamRegistrationWizardComponent);
        const component = fixture.componentInstance;
        component.step.set(2);
        component.prevStep();
        expect(component.step()).toBe(1);
    });
});
