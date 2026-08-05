import { afterNextRender, ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import QRCode from 'qrcode';

type Platform = 'ios' | 'android' | 'desktop';

/**
 * Game-Day panel — the rich, self-assembling capture of the hand-authored
 * "Schedules Now Available!" bulletin. Driven by the SAME isolated signal that
 * gates the public "View Schedule" card (schedule published AND games exist), so
 * every event gets it for free and it can never drift out of sync: it lights up
 * the instant a schedule is live and goes dark when it isn't.
 *
 * It does what the bulletin did — web schedule CTA + promote the free TSIC-Events
 * mobile app (what thousands actually use on game day) — but device-aware: the
 * viewer's own platform store badge leads, the other follows. No per-event
 * authoring, no stale links.
 */
@Component({
	selector: 'app-game-day-panel',
	standalone: true,
	imports: [RouterLink],
	templateUrl: './game-day-panel.component.html',
	styleUrl: './game-day-panel.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameDayPanelComponent {
	/** Absolute, jobPath-prefixed base (the jobPath is in the path, so it's preserved). */
	readonly jobPath = input.required<string>();

	// LIVE-ONLY panel. It used to serve the concluded phase too, via a `live` input that
	// switched the icon, the label and the app promo — but concluded stripped it to a header
	// plus one button wearing the same flag icon as the Event Status card sitting right above
	// it. That link now lives INSIDE Event Status, the band gates this panel on live, and the
	// former concluded branches are gone rather than left unreachable.
	protected readonly title = 'Schedule Links';
	protected readonly webLabel = 'View Schedule';

	/** Canonical TSIC-Events store URLs — one app serves every event, so these are
	 *  fixed (same IDs the legacy schedule views and the mobile app itself use). */
	protected readonly iosUrl = 'https://itunes.apple.com/app/id1550380490';
	protected readonly androidUrl = 'https://play.google.com/store/apps/details?id=com.teamsportsinfo.tsicevents';

	protected readonly scheduleLink = computed(() => `/${this.jobPath()}/schedule`);

	/** The viewer's platform — drives which store badge leads (primary emphasis).
	 *  Best-effort UA sniff; desktop is the safe default (shows both equally). */
	protected readonly platform: Platform = this.detectPlatform();

	/** Store-install QR codes (PNG data URLs), generated client-side. A store URL is
	 *  OS-specific, so each platform needs its own code — App Store and Google Play.
	 *  Static content (one app serves every event). Null until ready / on failure. */
	private readonly iosQr = signal<string | null>(null);
	private readonly androidQr = signal<string | null>(null);
	protected readonly iosQrSrc = this.iosQr.asReadonly();
	protected readonly androidQrSrc = this.androidQr.asReadonly();

	/** The scan-to-install QRs only make sense on desktop — pointless on the phone the
	 *  visitor is already holding (they'd tap the store badge instead). */
	protected readonly showQr = computed(() =>
		this.platform === 'desktop' && (this.iosQr() !== null || this.androidQr() !== null));

	constructor() {
		// Generate once after first render. Desktop-only — phones tap the badges, the
		// QRs are hidden there. Dark-on-white fixed colors so the codes stay scannable
		// under any theme (they sit on white tiles).
		afterNextRender(() => {
			if (this.platform !== 'desktop') return;
			QRCode.toDataURL(this.iosUrl, { width: 220, margin: 1, errorCorrectionLevel: 'M', color: { dark: '#0f172a', light: '#ffffff' } })
				.then(u => this.iosQr.set(u)).catch(() => this.iosQr.set(null));
			QRCode.toDataURL(this.androidUrl, { width: 220, margin: 1, errorCorrectionLevel: 'M', color: { dark: '#0f172a', light: '#ffffff' } })
				.then(u => this.androidQr.set(u)).catch(() => this.androidQr.set(null));
		});
	}

	private detectPlatform(): Platform {
		const ua = globalThis.navigator?.userAgent ?? '';
		if (/iphone|ipad|ipod/i.test(ua)) return 'ios';
		if (/android/i.test(ua)) return 'android';
		return 'desktop';
	}
}
