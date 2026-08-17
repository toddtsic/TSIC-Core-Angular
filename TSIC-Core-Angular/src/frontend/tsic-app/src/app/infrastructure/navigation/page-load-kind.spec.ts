import { wasPageReloaded } from './page-load-kind';

/**
 * The session rule keys on this one function. It must say "reloaded" ONLY for a reload —
 * anything else (arrived from outside, unknown, API missing) must read as NOT reloaded so
 * the guard logs out. Fail-closed is the invariant.
 */
describe('wasPageReloaded', () => {
    let getEntries: ReturnType<typeof vi.spyOn>;

    beforeEach(() => {
        getEntries = vi.spyOn(performance, 'getEntriesByType');
    });

    afterEach(() => vi.restoreAllMocks());

    const entry = (type: string) => [{ type } as unknown as PerformanceNavigationTiming];

    it('true for a reload', () => {
        getEntries.mockReturnValue(entry('reload'));
        expect(wasPageReloaded()).toBe(true);
    });

    it('false for a fresh navigation (emailed link, typed URL, new tab)', () => {
        getEntries.mockReturnValue(entry('navigate'));
        expect(wasPageReloaded()).toBe(false);
    });

    it('false for back/forward from another site', () => {
        getEntries.mockReturnValue(entry('back_forward'));
        expect(wasPageReloaded()).toBe(false);
    });

    it('falls back to the legacy PerformanceNavigation API when the modern one is empty', () => {
        getEntries.mockReturnValue([]);
        const legacy = { type: 1 }; // TYPE_RELOAD
        const perf = performance as unknown as { navigation?: unknown };
        const original = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(performance), 'navigation');
        Object.defineProperty(performance, 'navigation', { value: legacy, configurable: true });
        try {
            expect(wasPageReloaded()).toBe(true);
            legacy.type = 0; // TYPE_NAVIGATE
            expect(wasPageReloaded()).toBe(false);
        } finally {
            delete (perf as { navigation?: unknown }).navigation;
            if (original) Object.defineProperty(Object.getPrototypeOf(performance), 'navigation', original);
        }
    });

    it('fails CLOSED (not reloaded) when neither API is usable', () => {
        getEntries.mockReturnValue([]);
        Object.defineProperty(performance, 'navigation', { value: undefined, configurable: true });
        try {
            expect(wasPageReloaded()).toBe(false);
        } finally {
            delete (performance as unknown as { navigation?: unknown }).navigation;
        }
    });

    it('fails CLOSED when the API throws', () => {
        getEntries.mockImplementation(() => { throw new Error('nope'); });
        expect(wasPageReloaded()).toBe(false);
    });
});
