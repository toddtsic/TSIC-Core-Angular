import { Injectable, signal } from '@angular/core';

export interface ChildProfileDraft {
    firstName: string;
    lastName: string;
    dob?: string;
}

@Injectable({ providedIn: 'root' })
export class FamilyAccountWizardService {
    // Account basics (placeholder only; real implementation will call API)
    parentFirstName = signal<string>('');
    parentLastName = signal<string>('');
    email = signal<string>('');
    password = signal<string>('');

    // Children
    children = signal<ChildProfileDraft[]>([]);

    reset(): void {
        this.parentFirstName.set('');
        this.parentLastName.set('');
        this.email.set('');
        this.password.set('');
        this.children.set([]);
    }
}
