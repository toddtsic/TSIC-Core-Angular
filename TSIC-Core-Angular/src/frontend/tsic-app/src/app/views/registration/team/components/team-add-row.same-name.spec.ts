import type { SameNameEventTeamDto } from '@core/api';
import { joinNames, repsHoldingTeam } from './team-add-row.component';

/**
 * TEAM WIZARD — SAME-NAME DUPLICATE WARNING (Todd 2026-10-06)
 *
 * Another rep of the same club name may already have registered a team in this event. The add row
 * warns loudly and asks before adding: a second entry is a second fee and a headache for the
 * tournament. These are the rules for "the same team" and how the reps are named.
 */
const held = (teamName: string, repName: string, gradYear: string | null = '2030'): SameNameEventTeamDto => ({
    teamName, repName, gradYear, ageGroupName: '2030',
});

describe('repsHoldingTeam', () => {
    it('matches the name ignoring case and spacing', () => {
        const teams = [held('Fury 2030 Blue', 'Jane Smith')];
        expect(repsHoldingTeam(teams, '  fury  2030 BLUE ', '2030')).toEqual(['Jane Smith']);
    });

    it('a different grad year is a different team', () => {
        const teams = [held('Fury Blue', 'Jane Smith', '2030')];
        expect(repsHoldingTeam(teams, 'Fury Blue', '2031')).toEqual([]);
    });

    it('a grad year missing on either side does not set them apart', () => {
        expect(repsHoldingTeam([held('Fury Blue', 'Jane Smith', null)], 'Fury Blue', '2030')).toEqual(['Jane Smith']);
        expect(repsHoldingTeam([held('Fury Blue', 'Jane Smith', '2030')], 'Fury Blue', 'N/A')).toEqual(['Jane Smith']);
    });

    it('names every rep holding it, each once', () => {
        const teams = [
            held('Fury Blue', 'Jane Smith'),
            held('Fury Blue', 'Bob Jones'),
            held('Fury Blue', 'Jane Smith'),
            held('Fury Gold', 'Ann Lee'),
        ];
        expect(repsHoldingTeam(teams, 'Fury Blue', '2030')).toEqual(['Jane Smith', 'Bob Jones']);
    });

    it('an empty name holds nothing', () => {
        expect(repsHoldingTeam([held('Fury Blue', 'Jane Smith')], '  ', '2030')).toEqual([]);
    });

    it('a rep with no name on file still warns', () => {
        expect(repsHoldingTeam([held('Fury Blue', ' ')], 'Fury Blue', '2030')).toEqual(['another rep']);
    });
});

describe('joinNames', () => {
    it('reads naturally for one, two and many', () => {
        expect(joinNames(['Jane'])).toBe('Jane');
        expect(joinNames(['Jane', 'Bob'])).toBe('Jane and Bob');
        expect(joinNames(['Jane', 'Bob', 'Ann'])).toBe('Jane, Bob and Ann');
    });
});
