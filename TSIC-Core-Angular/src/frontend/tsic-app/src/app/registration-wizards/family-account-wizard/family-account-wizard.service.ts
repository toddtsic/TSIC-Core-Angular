import { Injectable, signal } from '@angular/core';

export interface ChildProfileDraft {
    firstName: string;
    lastName: string;
    dob?: string;
    gender: string;
    email?: string;
    phone?: string;
}

@Injectable({ providedIn: 'root' })
export class FamilyAccountWizardService {
    // Account basics (placeholder only; real implementation will call API)
    parentFirstName = signal<string>('');
    parentLastName = signal<string>('');
    email = signal<string>('');
    username = signal<string>('');
    password = signal<string>('');

    // Mode: 'create' = creating a new Family Account, 'edit' = editing existing
    mode = signal<'create' | 'edit'>('create');

    // Address (AspNetUser profile fields)
    address1 = signal<string>('');
    address2 = signal<string>('');
    city = signal<string>('');
    state = signal<string>('');
    postalCode = signal<string>('');

    // Parent contact info
    parent1FirstName = signal<string>('');
    parent1LastName = signal<string>('');
    parent1Phone = signal<string>('');
    parent1Carrier = signal<string>('');
    parent1Email = signal<string>('');
    parent1EmailConfirm = signal<string>('');

    parent2FirstName = signal<string>('');
    parent2LastName = signal<string>('');
    parent2Phone = signal<string>('');
    parent2Carrier = signal<string>('');
    parent2Email = signal<string>('');
    parent2EmailConfirm = signal<string>('');

    // Children
    children = signal<ChildProfileDraft[]>([]);

    addChild(child: ChildProfileDraft): void {
        const next = [...this.children(), child];
        this.children.set(next);
    }

    removeChildAt(index: number): void {
        const list = [...this.children()];
        if (index >= 0 && index < list.length) {
            list.splice(index, 1);
            this.children.set(list);
        }
    }

    reset(): void {
        this.parentFirstName.set('');
        this.parentLastName.set('');
        this.email.set('');
        this.username.set('');
        this.password.set('');
        this.mode.set('create');
        this.address1.set('');
        this.address2.set('');
        this.city.set('');
        this.state.set('');
        this.postalCode.set('');
        this.parent1FirstName.set('');
        this.parent1LastName.set('');
        this.parent1Phone.set('');
        this.parent1Carrier.set('');
        this.parent1Email.set('');
        this.parent1EmailConfirm.set('');
        this.parent2FirstName.set('');
        this.parent2LastName.set('');
        this.parent2Phone.set('');
        this.parent2Carrier.set('');
        this.parent2Email.set('');
        this.parent2EmailConfirm.set('');
        this.children.set([]);
    }
}
