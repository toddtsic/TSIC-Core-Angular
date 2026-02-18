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
    private readonly _parentFirstName = signal<string>('');
    private readonly _parentLastName = signal<string>('');
    private readonly _email = signal<string>('');
    private readonly _username = signal<string>('');
    private readonly _password = signal<string>('');
    readonly parentFirstName = this._parentFirstName.asReadonly();
    readonly parentLastName = this._parentLastName.asReadonly();
    readonly email = this._email.asReadonly();
    readonly username = this._username.asReadonly();
    readonly password = this._password.asReadonly();

    // Mode: 'create' = creating a new Family Account, 'edit' = editing existing
    private readonly _mode = signal<'create' | 'edit'>('create');
    readonly mode = this._mode.asReadonly();

    // Address (AspNetUser profile fields)
    private readonly _address1 = signal<string>('');
    private readonly _address2 = signal<string>('');
    private readonly _city = signal<string>('');
    private readonly _state = signal<string>('');
    private readonly _postalCode = signal<string>('');
    readonly address1 = this._address1.asReadonly();
    readonly address2 = this._address2.asReadonly();
    readonly city = this._city.asReadonly();
    readonly state = this._state.asReadonly();
    readonly postalCode = this._postalCode.asReadonly();

    // Parent contact info
    private readonly _parent1FirstName = signal<string>('');
    private readonly _parent1LastName = signal<string>('');
    private readonly _parent1Phone = signal<string>('');
    private readonly _parent1Email = signal<string>('');
    private readonly _parent1EmailConfirm = signal<string>('');
    readonly parent1FirstName = this._parent1FirstName.asReadonly();
    readonly parent1LastName = this._parent1LastName.asReadonly();
    readonly parent1Phone = this._parent1Phone.asReadonly();
    readonly parent1Email = this._parent1Email.asReadonly();
    readonly parent1EmailConfirm = this._parent1EmailConfirm.asReadonly();

    private readonly _parent2FirstName = signal<string>('');
    private readonly _parent2LastName = signal<string>('');
    private readonly _parent2Phone = signal<string>('');
    private readonly _parent2Email = signal<string>('');
    private readonly _parent2EmailConfirm = signal<string>('');
    readonly parent2FirstName = this._parent2FirstName.asReadonly();
    readonly parent2LastName = this._parent2LastName.asReadonly();
    readonly parent2Phone = this._parent2Phone.asReadonly();
    readonly parent2Email = this._parent2Email.asReadonly();
    readonly parent2EmailConfirm = this._parent2EmailConfirm.asReadonly();

    // Children
    private readonly _children = signal<ChildProfileDraft[]>([]);
    readonly children = this._children.asReadonly();

    // --- Controlled mutators ---
    setMode(v: 'create' | 'edit'): void { this._mode.set(v); }
    setCredentials(username: string, password: string): void {
        this._username.set(username);
        this._password.set(password);
    }
    setUsername(v: string): void { this._username.set(v); }
    setParent1(first: string, last: string, phone: string, email: string, emailConfirm: string): void {
        this._parent1FirstName.set(first);
        this._parent1LastName.set(last);
        this._parent1Phone.set(phone);
        this._parent1Email.set(email);
        this._parent1EmailConfirm.set(emailConfirm);
    }
    setParent2(first: string, last: string, phone: string, email: string, emailConfirm: string): void {
        this._parent2FirstName.set(first);
        this._parent2LastName.set(last);
        this._parent2Phone.set(phone);
        this._parent2Email.set(email);
        this._parent2EmailConfirm.set(emailConfirm);
    }
    setAddress(address1: string, city: string, state: string, postalCode: string): void {
        this._address1.set(address1);
        this._city.set(city);
        this._state.set(state);
        this._postalCode.set(postalCode);
    }
    setChildren(kids: ChildProfileDraft[]): void { this._children.set(kids); }

    addChild(child: ChildProfileDraft): void {
        const next = [...this._children(), child];
        this._children.set(next);
    }

    removeChildAt(index: number): void {
        const list = [...this._children()];
        if (index >= 0 && index < list.length) {
            list.splice(index, 1);
            this._children.set(list);
        }
    }

    reset(): void {
        this._parentFirstName.set('');
        this._parentLastName.set('');
        this._email.set('');
        this._username.set('');
        this._password.set('');
        this._mode.set('create');
        this._address1.set('');
        this._address2.set('');
        this._city.set('');
        this._state.set('');
        this._postalCode.set('');
        this._parent1FirstName.set('');
        this._parent1LastName.set('');
        this._parent1Phone.set('');
        this._parent1Email.set('');
        this._parent1EmailConfirm.set('');
        this._parent2FirstName.set('');
        this._parent2LastName.set('');
        this._parent2Phone.set('');
        this._parent2Email.set('');
        this._parent2EmailConfirm.set('');
        this._children.set([]);
    }
}
