import { NavigationError } from '@angular/router';
import { chunkLoadRecoveryHandler, isChunkLoadError } from './chunk-load-recovery';
import { selfHealReload } from './self-heal-reload';

describe('chunk-load-recovery', () => {
    describe('isChunkLoadError', () => {
        it.each([
            ['webpack name', { name: 'ChunkLoadError', message: '' }],
            ['webpack message', { message: 'Loading chunk 123 failed.' }],
            ['Chrome dynamic import', { message: 'Failed to fetch dynamically imported module: https://x/y-ABC.js' }],
            ['Firefox dynamic import', { message: 'error loading dynamically imported module: https://x/y-ABC.js' }],
            ['Safari dynamic import', { message: 'Importing a module script failed.' }],
        ])('recognizes %s', (_label, err) => {
            expect(isChunkLoadError(err)).toBe(true);
        });

        it('ignores unrelated errors and nullish', () => {
            expect(isChunkLoadError(new Error('Cannot read properties of undefined'))).toBe(false);
            expect(isChunkLoadError(null)).toBe(false);
            expect(isChunkLoadError(undefined)).toBe(false);
        });
    });

    describe('chunkLoadRecoveryHandler', () => {
        let request: ReturnType<typeof vi.spyOn>;
        beforeEach(() => {
            request = vi.spyOn(selfHealReload, 'request').mockReturnValue(true);
        });
        afterEach(() => vi.restoreAllMocks());

        const navError = (url: string, error: unknown) =>
            new NavigationError(1, url, error);

        it('on a chunk-load failure, self-heal reloads onto the URL the user was heading to', () => {
            chunkLoadRecoveryHandler(navError('/job/registration/team', {
                message: 'Failed to fetch dynamically imported module: https://x/team-OLD.js',
            }));
            expect(request).toHaveBeenCalledWith('/job/registration/team');
        });

        it('leaves any non-chunk navigation error alone', () => {
            chunkLoadRecoveryHandler(navError('/job/home', new Error('guard threw')));
            expect(request).not.toHaveBeenCalled();
        });
    });
});
