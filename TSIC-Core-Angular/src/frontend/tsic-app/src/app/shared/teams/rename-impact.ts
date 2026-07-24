import type { ClubAffectedJob } from '@core/api';

/**
 * HTML body for the club-linked team rename confirm dialog — old → new plus the affected-jobs
 * list from GET team-search/{teamId}/rename-impact. Shared by every admin surface that can
 * rename a club-linked team (Search Teams, LADT, Pairings, Schedule Hub) so the warning reads
 * identically everywhere. Names are escaped — they are club-typed text going into innerHTML.
 */
export function buildRenameImpactMessage(oldName: string, newName: string, jobs: ClubAffectedJob[]): string {
	const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
	let msg = `<p>Rename <strong>${esc(oldName)}</strong> to <strong>${esc(newName)}</strong>?</p>`;
	if (jobs.length > 0) {
		msg += `<p class='mb-1'>This team plays in <strong>${jobs.length} scheduled job${jobs.length !== 1 ? 's' : ''}</strong>. `
			+ `Every game name in these schedules will be rewritten — including hand-typed bracket and consolation game names.</p>`
			+ `<ul class='mb-0'>`
			+ jobs.map(j => `<li>${esc(j.jobName)} <span class='text-muted'>(${j.teamCount} team${j.teamCount !== 1 ? 's' : ''})</span></li>`).join('')
			+ `</ul>`;
	} else {
		msg += `<p class='text-muted small mb-0'>No other scheduled jobs — this updates the club's team library and this event.</p>`;
	}
	return msg;
}
