/**
 * Job type ids — mirrors backend TSIC.Domain.Constants.JobConstants.
 * The numeric values are the Jobs.JobTypeId column.
 */
export const JobType = {
	Root: 0,
	Club: 1,
	Tournament: 2,
	League: 3,
	Camp: 4,
	Sales: 5,
} as const;

/**
 * In a tournament the TEAM is the registering entity; an individual player
 * doesn't "register" the event, they join (self-roster onto) an already-
 * registered team. Public/editor copy reflects that for tournaments only.
 */
export function isTournament(jobTypeId: number | null | undefined): boolean {
	return jobTypeId === JobType.Tournament;
}
