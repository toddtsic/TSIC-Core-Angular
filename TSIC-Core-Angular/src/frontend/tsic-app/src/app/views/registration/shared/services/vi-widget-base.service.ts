import { inject, signal } from '@angular/core';
import { ViDarkModeService } from './vi-dark-mode.service';
import type {
    VIOfferData,
    VIQuoteObject,
    VIWidgetCallbacks,
    VIWidgetInstance,
    VIWidgetState,
    VIWindowExtension,
} from '../types/wizard.types';

/**
 * Shared base for the player and team insurance services.
 *
 * Owns the entire VerticalInsure widget integration — SDK constructor wiring,
 * widget-state signals (`widgetInitialized`, `hasUserResponse`, `quotes`),
 * error capture (`widgetError`), lifecycle (`reset` / `resetWidgetInit`), and
 * dark-mode dance.
 *
 * Subclasses add only their unique pieces: the purchase HTTP call (different
 * endpoint + request shape per flow) and the quote-display formatter.
 *
 * Why a base class rather than a shared injectable: each insurance service is
 * `providedIn: 'root'`, and we want each subclass instance to own its OWN
 * widget state — a singleton helper would accidentally share quotes/response
 * between flows. Base-class state is per-subclass-instance, which is what we
 * want.
 *
 * Callback wiring uses the SDK's OBJECT form (3rd arg as object) — the
 * function form (3rd=fn, 4th=fn) gives no path to `onError`. SDK callback
 * names are `onOfferStateChange` / `onOfferReady` / `onError` per the
 * `@vertical-insure/embedded-offer` source.
 */
export abstract class BaseInsuranceService {
    private readonly viDarkMode = inject(ViDarkModeService);

    protected readonly _quotes = signal<VIQuoteObject[]>([]);
    private readonly _hasUserResponse = signal(false);
    private readonly _widgetInitialized = signal(false);
    private readonly _widgetError = signal<string | null>(null);
    /** Live SDK instance, retained so a remount can tear down the prior widget
     *  before constructing the next one. The SDK injects its DOM INTO the host
     *  element rather than replacing it, so without an explicit teardown a
     *  remount (discount re-quote, payment toggle without an `@if` host swap)
     *  stacks a second widget beside the stale one. */
    private _instance: VIWidgetInstance | null = null;
    /** Concurrency flag for the per-flow purchase HTTP call. Subclasses set
     *  it true at the start of their purchase method and false in finally. */
    protected readonly purchasing = signal(false);

    readonly quotes = this._quotes.asReadonly();
    readonly hasUserResponse = this._hasUserResponse.asReadonly();
    readonly widgetInitialized = this._widgetInitialized.asReadonly();
    /** User-facing error string when the widget cannot load or the SDK reports
     *  an error. When set, callers should treat the offer as unavailable —
     *  surface the message and unlock submission. */
    readonly widgetError = this._widgetError.asReadonly();
    /** Backward-compat alias of widgetError for callers that read `.error()`. */
    readonly error = this._widgetError.asReadonly();

    /** Mount the VerticalInsure widget at `hostSelector` with the offer payload.
     *  No-op if already initialized — call `resetWidgetInit()` first to remount. */
    initWidget(hostSelector: string, offerData: VIOfferData): void {
        if (this._widgetInitialized()) return;
        const viWindow = globalThis as unknown as VIWindowExtension;
        if (!viWindow.VerticalInsure) {
            this.markWidgetUnavailable('Insurance offer service is unavailable. Please refresh and try again.');
            return;
        }
        try {
            // Tear down any widget from a prior mount before re-mounting. The SDK appends its
            // DOM into the host, so on a remount (discount re-quote) we must destroy the old
            // instance AND empty the host — otherwise the stale widget stays stacked above the new one.
            this.teardownWidget(hostSelector);

            this.viDarkMode.injectDarkModeColors(offerData);

            // Forward-declare so the callbacks can reference the instance once
            // the constructor returns. Callbacks won't fire until after mount.
            let instance!: VIWidgetInstance;

            const callbacks: VIWidgetCallbacks = {
                onOfferStateChange: (st: VIWidgetState) => {
                    instance.validate().then((valid: boolean) => {
                        this._hasUserResponse.set(valid);
                        this._quotes.set(st?.quotes ?? []);
                        this.viDarkMode.applyViDarkMode(hostSelector);
                    });
                },
                onOfferReady: () => {
                    this._widgetInitialized.set(true);
                    instance.validate().then((valid: boolean) => {
                        this._hasUserResponse.set(valid);
                    });
                    this.viDarkMode.applyViDarkMode(hostSelector);
                },
                onError: (err: unknown) => {
                    console.error('[VerticalInsure] widget reported error', err);
                    this.markWidgetUnavailable(this.formatWidgetError(err));
                },
            };

            instance = new viWindow.VerticalInsure!(hostSelector, offerData, callbacks);
            this._instance = instance;
        } catch (e: unknown) {
            console.error('[VerticalInsure] init threw', e);
            this.markWidgetUnavailable(this.formatWidgetError(e));
        }
    }

    /** Total premium across all quotes, in dollars. */
    premiumTotal(): number {
        return this._quotes().reduce((sum, q) => sum + Number(q?.total ?? 0), 0) / 100;
    }

    /** Allow the widget to be re-mounted (navigation back/forward, payment-method toggle). */
    resetWidgetInit(): void {
        this._widgetInitialized.set(false);
    }

    /** Full reset — clears quotes, response, error, init flag, purchasing, and dark-mode observer. */
    reset(): void {
        this._quotes.set([]);
        this._hasUserResponse.set(false);
        this._widgetInitialized.set(false);
        this._widgetError.set(null);
        this.purchasing.set(false);
        this.viDarkMode.disconnect();
    }

    /** Destroy the prior SDK instance and empty the host element. Safe to call when no widget
     *  is mounted. Called at the top of `initWidget` so every remount starts from a clean host. */
    private teardownWidget(hostSelector: string): void {
        try {
            this._instance?.destroy?.();
        } catch (e: unknown) {
            console.warn('[VerticalInsure] destroy() threw during teardown', e);
        }
        this._instance = null;
        const host = document.querySelector(hostSelector);
        if (host) host.replaceChildren();
    }

    /** Mark the widget as unavailable: stop the spinner, unlock the submit gate
     *  (treating the failure as an implicit "no response from VI"), and surface
     *  the message. */
    private markWidgetUnavailable(message: string): void {
        this._widgetError.set(message);
        this._widgetInitialized.set(true);
        this._hasUserResponse.set(true);
    }

    private formatWidgetError(e: unknown): string {
        if (!e) return 'Insurance is unavailable for this session.';
        if (e instanceof Error) return e.message;
        if (typeof e === 'string') return e;
        if (typeof e === 'object') {
            const obj = e as { message?: unknown; error?: unknown; statusText?: unknown; eventType?: unknown };
            if (typeof obj.message === 'string' && obj.message) return obj.message;
            if (typeof obj.error === 'string' && obj.error) return obj.error;
            if (typeof obj.statusText === 'string' && obj.statusText) return obj.statusText;
            if (typeof obj.eventType === 'string' && obj.eventType) return obj.eventType;
            try { return JSON.stringify(e); } catch { /* fall through */ }
        }
        return String(e);
    }
}
