import { Directive, Input, forwardRef, inject } from '@angular/core';
import { AbstractControl, NG_ASYNC_VALIDATORS, ValidationErrors } from '@angular/forms';
import { Observable, of, timer } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { UsLaxService, type UsLaxApiResponseDto } from './uslax.service';
import { RegistrationWizardService } from './registration-wizard.service';

@Directive({
    selector: '[usLaxValidator]',
    standalone: true,
    providers: [
        {
            provide: NG_ASYNC_VALIDATORS,
            useExisting: forwardRef(() => UsLaxValidatorDirective),
            multi: true
        }
    ]
})
export class UsLaxValidatorDirective {
    @Input('usLaxValidator') playerId!: string;

    private readonly svc = inject(UsLaxService);
    private readonly state = inject(RegistrationWizardService);

    validate(control: AbstractControl): Observable<ValidationErrors | null> {
        const value = String(control.value ?? '').trim();
        if (!value) {
            // Let required validator handle empties; clear any status
            if (this.playerId) {
                // Clear prior message; keep idle state
                this.state.setUsLaxResult(this.playerId, false);
            }
            return of(null);
        }

        // Begin validating
        if (this.playerId) this.state.setUsLaxValidating(this.playerId);

        const lastName = this.playerId ? (this.state.getPlayerLastName(this.playerId) || '') : '';
        const dob = this.playerId ? (this.state.getPlayerDob(this.playerId) || undefined) : undefined;
        const validThrough = this.state.getUsLaxValidThroughDate() || undefined;

        // Debounce 600ms to avoid over-calling
        return timer(600).pipe(
            switchMap(() => this.svc.validate(value, { lastName, dob, validThrough })),
            map(res => {
                let ok: boolean | undefined;
                let message: string | undefined;
                let membership: unknown;
                if (typeof (res as any)?.ok === 'boolean') {
                    ok = (res as any).ok; message = (res as any).message; membership = (res as any).membership;
                } else {
                    const verdict = this.computeLocalVerdict(res as any, lastName, dob, validThrough);
                    ok = verdict.ok; message = verdict.message; membership = verdict.membership;
                }
                if (this.playerId) this.state.setUsLaxResult(this.playerId, !!ok, message, membership);
                return ok ? null : { uslax: { message: message || 'Invalid membership' } };
            }),
            catchError(err => {
                const msg = err?.error?.message || 'Validation failed';
                if (this.playerId) this.state.setUsLaxResult(this.playerId, false, msg);
                return of({ uslax: { message: msg } });
            })
        );
    }

    private normalizeName(s: string): string {
        const base = (s || '').trim().toLowerCase();
        return base
            .replaceAll('’', '')
            .replaceAll("'", '')
            .replaceAll('`', '');
    }
    private sameDate(a?: Date, b?: Date): boolean {
        if (!a || !b) return false;
        return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
    }
    private toDate(v: any): Date | null {
        if (!v) return null;
        const d = v instanceof Date ? v : new Date(v);
        return Number.isNaN(d.getTime()) ? null : d;
    }
    private computeLocalVerdict(
        res: UsLaxApiResponseDto,
        lastName: string,
        dob?: Date,
        validThrough?: Date
    ): { ok: boolean; message?: string; membership?: any } {
        // Strictly adhere to observed response: { status_code, output: { ...fields } }
        const membership = res?.output ?? null;
        if (!membership) return { ok: false, message: 'No membership found' };

        const memLast = this.normalizeName((membership as any).lastname || '');
        const wantLast = this.normalizeName(lastName);

        const memDob = this.toDate((membership as any).birthdate);
        const wantDob = dob ? new Date(dob) : null;

        const memExp = this.toDate((membership as any).exp_date);
        const needThrough = validThrough ? new Date(validThrough) : null;

        // Require last name and DOB; only require expiration if a required-through date is specified
        if (!memLast || !memDob || (needThrough && !memExp))
            return { ok: false, message: 'Incomplete membership data', membership };

        if (memLast !== wantLast)
            return { ok: false, message: 'Last name mismatch', membership };

        if (!wantDob || !this.sameDate(memDob, wantDob))
            return { ok: false, message: 'DOB mismatch', membership };

        if (needThrough && memExp && memExp < needThrough)
            return { ok: false, message: 'Membership expires before required date', membership };

        return { ok: true, message: 'Valid membership', membership };
    }
}
