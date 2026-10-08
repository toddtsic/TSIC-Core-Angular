import type { SameNameEventTeamDto } from '@core/api';
import { otherRepsInAgeGroup } from './team-add-row.component';

/**
 * TEAM WIZARD — SAME-NAME DUPLICATE WARNING (Todd 2026-10-06)
 *
 * Another rep of the same club name may already have registered teams in this event. Once the add
 * row has an age group, it warns loudly — naming those reps and their teams in that age group — and
 * asks before adding. Compared by AGE GROUP, never team name: the same team carries different names
 * on different lists ("Top Tier National 2029" vs "2029").
 */
const held = (teamName: string, repName: string, ageGroupName = '2029'): SameNameEventTeamDto => ({
    teamName, repName, gradYear: null, ageGroupName,
});

describe('otherRepsInAgeGroup', () => {
    it('lists the teams other reps registered in the age group, whatever their names', () => {
        const teams = [held('Top Tier National 2029 (Blue)', 'Erin Abbott-Gillin'), held('2029', 'Erin Abbott-Gillin')];
        expect(otherRepsInAgeGroup(teams, '2029')).toEqual([
            { repName: 'Erin Abbott-Gillin', ageGroupName: '2029', teams: ['Top Tier National 2029 (Blue)', '2029'] },
        ]);
    });

    it('ignores other age groups', () => {
        expect(otherRepsInAgeGroup([held('Fury Blue', 'Jane Smith', '2030')], '2029')).toEqual([]);
    });

    it('a WAITLIST twin counts as its age group, on either side, and shows its true one', () => {
        expect(otherRepsInAgeGroup([held('Fury Blue', 'Jane Smith', 'WAITLIST - 2029')], '2029'))
            .toEqual([{ repName: 'Jane Smith', ageGroupName: 'WAITLIST - 2029', teams: ['Fury Blue'] }]);
        expect(otherRepsInAgeGroup([held('Fury Blue', 'Jane Smith', '2029')], 'WAITLIST - 2029'))
            .toEqual([{ repName: 'Jane Smith', ageGroupName: '2029', teams: ['Fury Blue'] }]);
    });

    it('a rep with teams in an age group and its waitlist gets one line per true age group', () => {
        const teams = [held('2029 Blue', 'Jane Smith', '2029'), held('LI 2029', 'Jane Smith', 'WAITLIST - 2029')];
        expect(otherRepsInAgeGroup(teams, '2029')).toEqual([
            { repName: 'Jane Smith', ageGroupName: '2029', teams: ['2029 Blue'] },
            { repName: 'Jane Smith', ageGroupName: 'WAITLIST - 2029', teams: ['LI 2029'] },
        ]);
    });

    it('matches the age group ignoring case and spacing', () => {
        expect(otherRepsInAgeGroup([held('Fury Blue', 'Jane Smith', '2029  Boys')], ' 2029 boys ')).toHaveLength(1);
    });

    it('lists each rep once, with all of their teams', () => {
        const teams = [
            held('Fury Blue', 'Jane Smith'),
            held('Fury Gold', 'Bob Jones'),
            held('Fury White', 'Jane Smith'),
        ];
        expect(otherRepsInAgeGroup(teams, '2029')).toEqual([
            { repName: 'Jane Smith', ageGroupName: '2029', teams: ['Fury Blue', 'Fury White'] },
            { repName: 'Bob Jones', ageGroupName: '2029', teams: ['Fury Gold'] },
        ]);
    });

    it('no age group yet, no warning', () => {
        expect(otherRepsInAgeGroup([held('Fury Blue', 'Jane Smith')], '')).toEqual([]);
    });

    it('a rep with no name on file still warns', () => {
        expect(otherRepsInAgeGroup([held('Fury Blue', ' ')], '2029')).toEqual([{ repName: 'another rep', ageGroupName: '2029', teams: ['Fury Blue'] }]);
    });
});
