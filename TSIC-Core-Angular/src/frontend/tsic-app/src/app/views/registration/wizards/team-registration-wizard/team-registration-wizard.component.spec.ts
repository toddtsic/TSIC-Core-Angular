import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { TeamRegistrationWizardComponent } from './team-registration-wizard.component';

describe('TeamRegistrationWizardComponent', () => {
    let component: TeamRegistrationWizardComponent;
    let fixture: ComponentFixture<TeamRegistrationWizardComponent>;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            declarations: [TeamRegistrationWizardComponent],
            imports: [ReactiveFormsModule]
        })
            .compileComponents();
    });

    beforeEach(() => {
        fixture = TestBed.createComponent(TeamRegistrationWizardComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should start at step 1', () => {
        expect(component.step).toBe(1);
    });

    it('should advance to step 2', () => {
        component.nextStep();
        expect(component.step).toBe(2);
    });

    it('should go back to step 1', () => {
        component.nextStep();
        component.prevStep();
        expect(component.step).toBe(1);
    });
});
