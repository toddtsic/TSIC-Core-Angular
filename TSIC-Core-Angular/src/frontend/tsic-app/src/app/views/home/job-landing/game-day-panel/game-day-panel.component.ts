import { afterNextRender, ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import QRCode from 'qrcode';
import { InlineGameClockComponent } from '@views/scheduling/view-schedule/components/inline-game-clock.component';
import { SmartMarkerComponent } from '@widgets/communications/smart-bulletins/smart-marker.component';

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
	imports: [RouterLink, InlineGameClockComponent, SmartMarkerComponent],
	templateUrl: './game-day-panel.component.html',
	styleUrl: './game-day-panel.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameDayPanelComponent {
	/** Absolute, jobPath-prefixed base (the jobPath is in the path, so it's preserved). */
	readonly jobPath = input.required<string>();

	/** True while the event is still upcoming/in-progress (live pulse + "live" copy);
	 *  false once concluded — the schedule becomes the FINAL record (no pulse, results
	 *  framing). The app promo stays useful either way (review scores/brackets). */
	readonly live = input<boolean>(true);

	/** Job id for the embedded live game clock (self-fetches its own state). */
	readonly jobId = input<string>('');

	/** Whether to mount the inline game clock (parent gates on game-clock games
	 *  existing). The clock self-hides when there's nothing active, and hides itself
	 *  on phones via its own media query. */
	readonly showClock = input<boolean>(false);

	protected readonly title = computed(() => this.live() ? 'Schedules Are Live' : 'Final Schedule & Results');
	protected readonly webLabel = computed(() => this.live() ? 'View Schedule' : 'View Final Schedule');

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
