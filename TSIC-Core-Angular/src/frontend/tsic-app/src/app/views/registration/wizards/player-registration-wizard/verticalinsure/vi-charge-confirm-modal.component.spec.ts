import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ViChargeConfirmModalComponent } from './vi-charge-confirm-modal.component';

describe('ViChargeConfirmModalComponent', () => {
    let fixture: ComponentFixture<ViChargeConfirmModalComponent>;
    let component: ViChargeConfirmModalComponent;

    beforeEach(() => {
        TestBed.configureTestingModule({
            imports: [ViChargeConfirmModalComponent]
        });
        fixture = TestBed.createComponent(ViChargeConfirmModalComponent);
        component = fixture.componentInstance;
    });

    it('renders insurance-only title when viCcOnlyFlow is true', () => {
        component.viCcOnlyFlow = true;
        component.quotedPlayers = ['Alice'];
        component.premiumTotal = 123.45;
        component.email = 'user@test.com';
        fixture.detectChanges();
        const title = fixture.nativeElement.querySelector('.modal-title').textContent.trim();
        expect(title).toBe('Confirm Insurance Purchase');
        expect(fixture.nativeElement.textContent).toContain('Alice');
        expect(fixture.nativeElement.textContent).toContain('123.45');
        expect(fixture.nativeElement.textContent).toContain('user@test.com');
    });

    it('renders combined title when viCcOnlyFlow is false', () => {
        component.viCcOnlyFlow = false;
        component.quotedPlayers = ['Bob', 'Charlie'];
        fixture.detectChanges();
        const title = fixture.nativeElement.querySelector('.modal-title').textContent.trim();
        expect(title).toBe('Confirm Registration Payment + Insurance');
        expect(fixture.nativeElement.textContent).toContain('Bob, Charlie');
    });

    it('emits confirmed on OK click', () => {
        const spy = jasmine.createSpy('confirmed');
        component.confirmed.subscribe(spy);
        fixture.detectChanges();
        const btn = fixture.nativeElement.querySelector('.btn-primary');
        btn.click();
        expect(spy).toHaveBeenCalled();
    });

    it('emits cancelled on CANCEL or close button click', () => {
        const spy = jasmine.createSpy('cancelled');
        component.cancelled.subscribe(spy);
        fixture.detectChanges();
        // First cancel button
        const btnCancel = fixture.nativeElement.querySelector('.btn-secondary');
        btnCancel.click();
        // Then header close button
        const btnClose = fixture.nativeElement.querySelector('.btn-close');
        btnClose.click();
        expect(spy).toHaveBeenCalledTimes(2);
    });
});
