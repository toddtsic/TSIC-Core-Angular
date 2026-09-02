import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UsLaxValidationService, type UsLaxMember } from '@infrastructure/services/uslax-validation.service';

/**
 * USA Lacrosse Number Tester — a LOOKUP, deliberately not a validator.
 *
 * It answers one question: what does USA Lacrosse currently hold for this membership number?
 * It does NOT decide whether the number is acceptable for an event. That decision is
 * UsLaxEligibilityPolicy's, it runs server-side, and it needs a real registrant's last name and
 * date of birth — which this page has no honest way to obtain. Typing them in by hand would only
 * test what the operator already assumes.
 *
 * This component previously carried its own partial copy of the rules (an Active check and an
 * expiry-vs-job-cutoff comparison), making it a third dialect alongside the registration form and
 * the reconciliation grid. Worse, its date comparison was raw `Date` objects: `exp_date` arrives
 * as a bare `2026-12-31` (UTC midnight) while the job cutoff arrives zoneless (LOCAL midnight), so
 * a membership expiring exactly ON the cutoff read as expired — the same class of defect as
 * 308b41219 and db2edcfed. All of it is gone rather than repaired: this page has no business
 * holding rules at all.
 */
@Component({
	selector: 'app-uslax-test',
	standalone: true,
	imports: [FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './uslax-test.component.html',
	styleUrl: './uslax-test.component.scss'
})
export class UsLaxTestComponent {
	private readonly usLaxService = inject(UsLaxValidationService);

	readonly membershipNumber = signal('');
	readonly isLoading = signal(false);
	readonly result = signal<UsLaxMember | null>(null);
	readonly errorMessage = signal<string | null>(null);
	readonly hasSearched = signal(false);

	/**
	 * Editing the number invalidates whatever the last attempt said. Without this the format
	 * error outlived the input that caused it — type "12345", get "must be 6 to 12 digits",
	 * finish typing a valid 12-digit number, and the rejection stayed on screen next to it.
	 */
	onNumberChange(value: string): void {
		this.membershipNumber.set(value);
		this.errorMessage.set(null);
	}

	lookup(): void {
		const num = this.membershipNumber().trim();
		if (!num) return;
		if (!/^\d{6,12}$/.test(num)) {
			this.errorMessage.set('Membership number must be 6 to 12 digits.');
			this.result.set(null);
			this.hasSearched.set(true);
			return;
		}

		this.isLoading.set(true);
		this.errorMessage.set(null);
		this.result.set(null);
		this.hasSearched.set(true);

		this.usLaxService.verify(num).subscribe({
			next: member => {
				if (member) {
					this.result.set(member);
				} else {
					this.errorMessage.set('No member data returned. Check the number and try again.');
				}
				this.isLoading.set(false);
			},
			error: () => {
				this.errorMessage.set('Validation service temporarily unavailable. Please try again later.');
				this.isLoading.set(false);
			}
		});
	}

	clear(): void {
		this.membershipNumber.set('');
		this.result.set(null);
		this.errorMessage.set(null);
		this.hasSearched.set(false);
	}

	onKeydown(event: KeyboardEvent): void {
		if (event.key === 'Enter') {
			this.lookup();
		}
	}
}
