import type { EmailBatchJobStatus } from '@core/api';

/**
 * The one way a finished batch email is reported to a human.
 *
 * One stat, what we did: every address on every email SES accepted, repeats included — a parent
 * with three players counts three times. It is the same figure the Email Log row records. Anything
 * not sent is a single count (the screen lists the addresses); not sent is not sent, no reasons.
 */
export function batchSentMessage(s: Pick<EmailBatchJobStatus, 'sentRecipients' | 'failedAddresses'>): string {
	const sent = (s.sentRecipients ?? 0).toLocaleString();
	const notSent = s.failedAddresses?.length ?? 0;
	return notSent > 0
		? `Sent to ${sent} email address(es) · ${notSent.toLocaleString()} not sent`
		: `Sent to ${sent} email address(es)`;
}
