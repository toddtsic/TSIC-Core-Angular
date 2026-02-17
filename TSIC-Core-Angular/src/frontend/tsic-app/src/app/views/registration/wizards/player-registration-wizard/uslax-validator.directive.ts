import { Directive, Input, forwardRef, inject } from '@angular/core';
import { AbstractControl, NG_ASYNC_VALIDATORS, ValidationErrors } from '@angular/forms';
import { Observable, of, timer } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { UsLaxService, type UsLaxApiResponseDto, type UsLaxMembershipDto } from './uslax.service';
import { RegistrationWizardService } from './registration-wizard.service';
import { environment } from '@environments/environment';

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

        // Shortcut: accept known test number without calling USA Lacrosse (dev only)
        if (environment.testUsLaxNumber && value === environment.testUsLaxNumber) {
            if (this.playerId) {
                this.state.setUsLaxResult(this.playerId, true, 'Test US Lax number accepted');
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
                const resRecord = res as unknown as Record<string, unknown>;
                if (typeof resRecord?.['ok'] === 'boolean') {
                    ok = resRecord['ok'] as boolean; message = resRecord['message'] as string | undefined; membership = resRecord['membership'];
                } else {
                    const verdict = this.computeLocalVerdict(res, lastName, dob, validThrough);
                    ok = verdict.ok; message = verdict.message; membership = verdict.membership;
                }
                if (this.playerId) this.state.setUsLaxResult(this.playerId, !!ok, message, membership as Record<string, unknown> | undefined);
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
    private toDate(v: unknown): Date | null {
        if (!v) return null;
        const d = v instanceof Date ? v : new Date(v as string | number);
        return Number.isNaN(d.getTime()) ? null : d;
    }
    private computeLocalVerdict(
        res: UsLaxApiResponseDto,
        lastName: string,
        dob?: Date,
        validThrough?: Date
    ): { ok: boolean; message?: string; membership?: UsLaxMembershipDto | null } {
        // Strictly adhere to observed response: { status_code, output: { ...fields } }
        const membership = res?.output ?? null;
        const guidanceHtml = `<strong>Beginning July 1, 2025 all USA Lacrosse player members will be required to complete the one-time age verification process to maintain active membership status. (<a href='https://www.usalacrosse.com/age-verification' target='_blank' rel='noopener'>Learn more</a>)</strong><br><br>The USA Lacrosse Number entered does not meet validation requirements. To pass validation:<ol><li>The membership must be <strong>Valid and Active</strong></li><li>The membership must not expire before the date required by the event or club director</li><li>The DOB and Last Name Spelling on your Family Account must match what USA Lacrosse has on file</li></ol><br>To look up your USA Lacrosse Number at USA Lacrosse, <a href='https://account.usalacrosse.com/login/lookup' target='_blank' rel='noopener'>CLICK HERE</a><br>To register for a USA Lacrosse Number at USA Lacrosse, <a href='https://www.usalacrosse.com/membership' target='_blank' rel='noopener'>CLICK HERE</a><br>For assistance please contact <a href='mailto:membership@usalacrosse.com'>membership@usalacrosse.com</a> or call 410-235-6882`;
        // If no membership record returned, treat number as invalid and avoid generic guidance/modal
        if (!membership) return { ok: false, message: 'The number you entered is not a valid USA Lacrosse number.', membership: undefined };

        const memLast = this.normalizeName(membership.lastname || '');
        const wantLast = this.normalizeName(lastName);

        const memDob = this.toDate(membership.birthdate);
        const wantDob = dob ? new Date(dob) : null;

        const memExp = this.toDate(membership.exp_date);
        const needThrough = validThrough ? new Date(validThrough) : null;

        // Require last name and DOB; only require expiration if a required-through date is specified
        // If membership data is incomplete from the API, surface the general guidance (still a USA Lacrosse-side issue)
        if (!memLast || !memDob || (needThrough && !memExp))
            return { ok: false, message: guidanceHtml, membership };

        // TSIC/entry mismatch: Last name doesn't match the player's TSIC profile; do not show generic guidance or API modal
        if (memLast !== wantLast)
            return { ok: false, message: 'The last name you entered in TSIC does not match the last name on file at USA Lacrosse for this number. Please correct the Last Name in your TSIC Family Account or enter the USA Lacrosse number for the matching player.', membership: undefined };

        // TSIC/entry mismatch: DOB mismatch; do not show generic guidance or API modal
        if (!wantDob || !this.sameDate(memDob, wantDob))
            return { ok: false, message: 'The date of birth in TSIC does not match the date of birth on file at USA Lacrosse for this number. Please verify and correct the DOB in your TSIC Family Account.', membership: undefined };

        // Event requirement not met: membership expires before the required-through date; do not show generic guidance or API modal
        if (needThrough && memExp && memExp < needThrough) {
            const exp = memExp.toLocaleDateString?.() || String(memExp);
            const req = needThrough.toLocaleDateString?.() || String(needThrough);
            return { ok: false, message: `The USA Lacrosse membership expires on ${exp}, which is before this event’s required date (${req}). Please renew the membership so it is valid through the required date.`, membership: undefined };
        }

        // Active status check
        const status = String(membership.mem_status || '').trim();
        if (status && status.toLowerCase() !== 'active') {
            return { ok: false, message: `Your USA Lacrosse account is NOT ACTIVE.<br><br>${guidanceHtml}`, membership };
        }

        return { ok: true, message: 'Valid membership', membership };
    }
}
