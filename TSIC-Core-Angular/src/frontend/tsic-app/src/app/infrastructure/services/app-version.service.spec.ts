import { TestBed } from '@angular/core/testing';
import { NavigationEnd, NavigationStart, Router, type Event as RouterEvent } from '@angular/router';
import { Subject } from 'rxjs';
import { environment } from '@environments/environment';
import { AppVersionService } from './app-version.service';
import { selfHealReload } from '../navigation/self-heal-reload';

/**
 * Scenario coverage (Todd's walk-through):
 *   • click a screen this tab already visited, after a deploy → reload once, onto that screen
 *   • click when nothing changed → no reload
 *   • server can't say (503 mid-cutover / old server hands back index.html) → no reload
 *   • ng serve → never even asks
 *   • deploy mid-copy (brake) → no reload storm
 */
describe('AppVersionService', () => {
    const COMPILED = 'v260817.1200.aaaaaaa';
    const NEWER = 'v260817.1300.bbbbbbb';

    let events: Subject<RouterEvent>;
    let currentNavigation: unknown;
    let fetchMock: ReturnType<typeof vi.fn>;
    let request: ReturnType<typeof vi.spyOn>;
    let service: AppVersionService;
    let originalStamp: string;

    /** A fetch response the service should read as "the server is serving <stamp>". */
    const jsonResponse = (stamp: string) =>
        new Response(JSON.stringify({ buildStamp: stamp }), {
            status: 200,
            headers: { 'content-type': 'application/json' },
        });

    const flush = () => new Promise<void>(r => setTimeout(r, 0));

    beforeEach(() => {
        events = new Subject<RouterEvent>();
        currentNavigation = null;
        fetchMock = vi.fn().mockResolvedValue(jsonResponse(COMPILED));
        vi.stubGlobal('fetch', fetchMock);
        request = vi.spyOn(selfHealReload, 'request').mockReturnValue(true);

        originalStamp = environment.buildVersion;
        (environment as { buildVersion: string }).buildVersion = COMPILED;

        TestBed.configureTestingModule({
            providers: [
                {
                    provide: Router,
                    useValue: {
                        events: events.asObservable(),
                        getCurrentNavigation: () => currentNavigation,
                    },
                },
            ],
        });
        service = TestBed.inject(AppVersionService);
    });

    afterEach(() => {
        (environment as { buildVersion: string }).buildVersion = originalStamp;
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it('asks the server on every URL change, with no-store', async () => {
        service.start();
        events.next(new NavigationStart(1, '/job/registration/team'));
        await flush();
        expect(fetchMock).toHaveBeenCalledTimes(1);
        const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
        expect(url).toMatch(/\/version\.json$/);
        expect(init.cache).toBe('no-store');
    });

    it('same build on the server → no reload', async () => {
        service.start();
        events.next(new NavigationStart(1, '/job/home'));
        await flush();
        events.next(new NavigationEnd(1, '/job/home', '/job/home'));
        expect(request).not.toHaveBeenCalled();
    });

    it('SCENARIO: newer build on the server, click a screen already in memory → reload ONCE, on arrival', async () => {
        fetchMock.mockResolvedValue(jsonResponse(NEWER));
        service.start();

        currentNavigation = {}; // navigation in flight
        events.next(new NavigationStart(1, '/job/registration/team'));
        await flush(); // fetch resolves while still navigating
        expect(request).not.toHaveBeenCalled(); // not yet — wait for arrival

        currentNavigation = null;
        events.next(new NavigationEnd(1, '/job/registration/team', '/job/registration/team'));
        expect(request).toHaveBeenCalledTimes(1);
        // No target: the URL bar already IS the destination at NavigationEnd; reload in place.
        expect(request).toHaveBeenCalledWith();
    });

    it('newer build detected AFTER a fast navigation already finished → reload immediately', async () => {
        // In-memory routes finish in ms; the fetch can outrun NavigationEnd. Router idle at
        // resolution → nothing to wait for.
        fetchMock.mockResolvedValue(jsonResponse(NEWER));
        service.start();
        currentNavigation = null; // router already idle when the fetch resolves
        events.next(new NavigationStart(1, '/job/home'));
        await flush();
        expect(request).toHaveBeenCalledTimes(1);
    });

    it('once stale, does not keep re-fetching; the armed reload fires on the next arrival', async () => {
        fetchMock.mockResolvedValue(jsonResponse(NEWER));
        service.start();
        currentNavigation = {};
        events.next(new NavigationStart(1, '/a'));
        await flush();
        events.next(new NavigationStart(2, '/b')); // stale already known
        await flush();
        expect(fetchMock).toHaveBeenCalledTimes(1);
        events.next(new NavigationEnd(2, '/b', '/b'));
        expect(request).toHaveBeenCalledTimes(1);
    });

    it('tab comes back into view → checks; stale + router idle → reload now', async () => {
        fetchMock.mockResolvedValue(jsonResponse(NEWER));
        service.start();
        Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
        document.dispatchEvent(new Event('visibilitychange'));
        await flush();
        expect(fetchMock).toHaveBeenCalledTimes(1);
        expect(request).toHaveBeenCalledTimes(1);
    });

    it('tab goes hidden → no check', async () => {
        service.start();
        Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true });
        document.dispatchEvent(new Event('visibilitychange'));
        await flush();
        expect(fetchMock).not.toHaveBeenCalled();
    });

    describe('never reloads on "don\'t know"', () => {
        it('503 (app pool stopped mid-cutover)', async () => {
            fetchMock.mockResolvedValue(new Response('', { status: 503 }));
            service.start();
            events.next(new NavigationStart(1, '/x'));
            await flush();
            events.next(new NavigationEnd(1, '/x', '/x'));
            expect(request).not.toHaveBeenCalled();
        });

        it('old server without version.json — SPA rewrite hands back index.html as 200 text/html', async () => {
            fetchMock.mockResolvedValue(new Response('<!doctype html><html></html>', {
                status: 200, headers: { 'content-type': 'text/html' },
            }));
            service.start();
            events.next(new NavigationStart(1, '/x'));
            await flush();
            events.next(new NavigationEnd(1, '/x', '/x'));
            expect(request).not.toHaveBeenCalled();
        });

        it('JSON without a usable stamp', async () => {
            fetchMock.mockResolvedValue(new Response('{"buildStamp":""}', {
                status: 200, headers: { 'content-type': 'application/json' },
            }));
            service.start();
            events.next(new NavigationStart(1, '/x'));
            await flush();
            events.next(new NavigationEnd(1, '/x', '/x'));
            expect(request).not.toHaveBeenCalled();
        });

        it('network failure', async () => {
            fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
            service.start();
            events.next(new NavigationStart(1, '/x'));
            await flush();
            events.next(new NavigationEnd(1, '/x', '/x'));
            expect(request).not.toHaveBeenCalled();
        });
    });

    it('under ng serve (buildVersion "dev") it never asks at all', async () => {
        (environment as { buildVersion: string }).buildVersion = 'dev';
        service.start();
        events.next(new NavigationStart(1, '/x'));
        await flush();
        expect(fetchMock).not.toHaveBeenCalled();
        expect(service.enabled()).toBe(false);
    });

    it('brake: if the self-heal reload is refused (deploy mid-copy), disarm instead of retrying every arrival', async () => {
        fetchMock.mockResolvedValue(jsonResponse(NEWER));
        request.mockReturnValue(false); // braked
        service.start();
        currentNavigation = {};
        events.next(new NavigationStart(1, '/a'));
        await flush();
        events.next(new NavigationEnd(1, '/a', '/a'));
        expect(request).toHaveBeenCalledTimes(1);
        // Disarmed: a further arrival with no new check does NOT call request again…
        events.next(new NavigationEnd(1, '/a', '/a'));
        expect(request).toHaveBeenCalledTimes(1);
        // …but the next URL change re-asks the server.
        events.next(new NavigationStart(2, '/b'));
        await flush();
        expect(fetchMock).toHaveBeenCalledTimes(2);
    });

    it('start() is idempotent', async () => {
        service.start();
        service.start();
        events.next(new NavigationStart(1, '/x'));
        await flush();
        expect(fetchMock).toHaveBeenCalledTimes(1);
    });
});
