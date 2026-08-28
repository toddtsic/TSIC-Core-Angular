import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HelpSearchService } from '@infrastructure/services/help-search.service';
import { HelpAudienceService } from '@infrastructure/services/help-audience.service';
import { PreparedDoc, rank } from '@infrastructure/services/help-search.ranking';
import type { HelpSearchHit } from '@infrastructure/services/help.types';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import type { HelpContent } from '@infrastructure/services/help.types';
import { HelpService } from '@infrastructure/services/help.service';
import { HelpContextService } from '@infrastructure/services/help-context.service';
import { HelpManifestService } from '@infrastructure/services/help-manifest.service';
import { ToastService } from '@shared-ui/toast.service';
import { ResizablePanelDirective } from '@shared-ui/directives/resizable-panel.directive';
import { HelpEditorComponent } from './help-editor.component';

interface HelpTab {
  readonly topic: string;
  readonly label: string;
  readonly icon: string;
}

/**
 * The single, app-wide "?" launcher. It reads the current route's help key (via HelpContextService)
 * and opens a right-side drawer with tabs for that page: Help (the authored explainer), FAQ (a growing
 * Q&A), and Pro Tips (power-user features, admin routes only). Each tab is a topic under the same
 * component — "overview", "faq", "pro-tips" — served as a static asset from
 * public/{component}/{topic}.html.
 *
 * Content renders with the app's own design-system styles, so illustrations look like the real product.
 * In LOCAL development the served files are the working tree, so the author sees a pencil that edits
 * whichever tab is active in the Syncfusion editor and writes the file directly (File System Access
 * API) — then it's committed and pushed like any change. FAQ is the tab that grows over time.
 */
@Component({
  selector: 'app-help-launcher',
  standalone: true,
  imports: [HelpEditorComponent, ResizablePanelDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './help-launcher.component.html',
  styleUrl: './help-launcher.component.scss',
})
export class HelpLauncherComponent {
  private readonly help = inject(HelpService);
  private readonly context = inject(HelpContextService);
  private readonly manifest = inject(HelpManifestService);
  private readonly toast = inject(ToastService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly search = inject(HelpSearchService);
  private readonly audience = inject(HelpAudienceService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  readonly tabs: readonly HelpTab[] = [
    { topic: 'overview', label: 'Help', icon: 'bi-life-preserver' },
    { topic: 'faq', label: 'FAQ', icon: 'bi-patch-question' },
    { topic: 'pro-tips', label: 'Pro Tips', icon: 'bi-lightning-charge' },
  ];

  readonly isOpen = signal(false);
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly content = signal<HelpContent | null>(null);
  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly activeTopic = signal<string>('overview');

  // ── Search across the whole manual, from inside the drawer ──────────────────────────────────
  readonly query = signal('');
  readonly searchActiveIndex = signal(0);
  readonly indexLoading = signal(false);
  private readonly docs = signal<readonly PreparedDoc[]>([]);

  /**
   * The corpus narrowed to what this user could actually navigate to. Both search and browse read
   * from here, so a family never sees "3rd Party Access" listed in either. Depends on the auth
   * signal via HelpAudienceService, so it re-narrows if the user switches role.
   */
  private readonly visibleDocs = computed(() =>
    this.docs().filter((p) => this.audience.canSee(p.doc.component))
  );

  /** True while the user is searching — the body shows results instead of the current page's help. */
  readonly searching = computed(() => this.query().trim().length >= 2);

  /** Explicitly browsing the table of contents ("All topics"), rather than reading a page. */
  readonly browsing = signal(false);

  /**
   * Whether the body is showing the MANUAL (results or contents) rather than one page's help.
   * Browsing and searching are the same surface at different zoom levels: the whole manual, or the
   * whole manual narrowed by a query. Treating them as one state is what keeps the drawer from
   * feeling like two different screens depending on whether the page you're on has help.
   *
   * "No page to show" is the third way into it, and it makes the dead panel unreachable rather than
   * merely unlikely: with no component there is nothing to render, so the manual is what's on
   * screen — whether that's a route with no help of its own, a "back" from a searched-to page, or
   * navigating to an unkeyed route while the drawer is open.
   */
  readonly showManual = computed(
    () => this.searching() || this.browsing() || !this.component()
  );

  /**
   * Ranked matches. A plain computed with no debounce: the corpus is already in memory and a full
   * pass measures well under a frame, so debouncing would only put lag between a keystroke and its
   * results. (Debounce belongs at a keystroke source that triggers HTTP — this triggers none.)
   */
  readonly results = computed<HelpSearchHit[]>(() => rank(this.visibleDocs(), this.query(), 25));

  /**
   * Every page in the manual, A–Z. Shown as the landing view on routes that have no help of their
   * own: the drawer's job there IS the manual, so it opens onto a table of contents rather than an
   * apology. One row per component — the overview is each page's front door; FAQ and Pro Tips are
   * reachable as tabs once you're there, and listing them here would treble the list for no gain.
   */
  readonly allTopics = computed(() =>
    this.visibleDocs()
      .filter((p) => p.doc.topic === 'overview')
      .map((p) => ({ component: p.doc.component, topic: p.doc.topic, title: p.doc.title }))
      .sort((a, b) => a.title.localeCompare(b.title))
  );

  // Per-topic cache so switching Help <-> FAQ doesn't refetch/flicker within a session.
  private readonly cache = new Map<string, HelpContent>();

  /** The component (page) for the current route, or null when the route declares no help key. */
  private readonly routeComponent = computed(() => {
    const raw = this.context.helpKey();
    return raw ? this.context.parseKey(raw).component : null;
  });

  /**
   * Set when the header's search box opens a topic that isn't this route's — the drawer then shows
   * that page's help instead. A linkedSignal, so it reseeds itself to null the moment the route's
   * help key changes: navigating away can't leave the drawer pinned to a stale topic, and no effect
   * is needed to clear it.
   */
  private readonly overrideComponent = linkedSignal<string | null, string | null>({
    source: () => this.routeComponent(),
    computation: () => null,
  });

  /** What the drawer is actually showing: a searched-to topic if there is one, else this route's. */
  readonly component = computed(() => this.overrideComponent() ?? this.routeComponent());

  /** The authored body, trusted for render. Content is authored locally and git-reviewed before deploy. */
  readonly safeHtml = computed<SafeHtml | null>(() => {
    const html = this.content()?.html;
    return html ? this.sanitizer.bypassSecurityTrustHtml(html) : null;
  });

  /**
   * Show the edit affordance only in local development, where the served public/help files ARE the
   * working tree. No auth gate: whoever runs the app locally is the author, and pre-auth pages (login,
   * role-selection) must be editable too. Staging/prod are read-only (manifest.canEdit is env-gated).
   */
  readonly canEdit = computed(() => this.manifest.canEdit() && !!this.component());

  /**
   * Show the "?" even on routes with no help of their own. Set by the DESKTOP header instance only:
   * there the drawer also carries search, so it always has something to offer. The mobile instance
   * leaves this false — search is desktop-only, so an always-on "?" there could open to a dead panel.
   */
  readonly alwaysShow = input(false);

  /** Whether THIS route has authored help of its own. Drives the drawer's landing view. */
  readonly hasPageHelp = computed(() => {
    // Route-derived deliberately, not component(): a searched-to page must not be mistaken for
    // this route having help of its own.
    const component = this.routeComponent();
    return !!component && this.manifest.hasComponent(component);
  });

  /**
   * Whether to show the "?" at all. Where search is present it is always shown; otherwise it hides
   * wherever the page has no content under any tab, rather than opening to nothing.
   */
  readonly available = computed(() => this.alwaysShow() || this.hasPageHelp());

  /** Fallback label derived from the component key, for before the index has loaded. */
  readonly pageLabel = computed(() => {
    const component = this.component();
    if (!component) return null;
    const spaced = component.replace(/-/g, ' ');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  });

  /**
   * component → the page's name, taken from the OVERVIEW topic specifically.
   *
   * Every topic file opens with its own <h3>, but only the overview's is the page's plain name:
   * the FAQ titles itself "Adult Registration — questions". Docs arrive sorted by key, so "faq"
   * precedes "overview" — a first-wins map silently picked the FAQ variant for all 12 pages that
   * have one. Prefer the overview explicitly; fall back to any topic only if it's missing.
   */
  private readonly titlesByComponent = computed(() => {
    const map = new Map<string, string>();
    for (const p of this.docs()) {
      if (p.doc.topic === 'overview' || !map.has(p.doc.component)) {
        map.set(p.doc.component, p.doc.title);
      }
    }
    return map;
  });

  /**
   * The drawer's heading: the page's OWN title, not a de-kebabbed folder name. "view-rosters" is
   * called "Your Team Roster" by the person who wrote it, and showing anything else made the header
   * disagree with the first line of the content — the thing that read as losing your place when
   * jumping between topics.
   */
  readonly pageTitle = computed(() => {
    const component = this.component();
    if (!component) return 'Help';
    return this.titlesByComponent().get(component) ?? this.pageLabel() ?? 'Help';
  });

  /**
   * Which tabs to show: a tab appears when it has content, or when it can be authored (local dev).
   * So on a deployed build a reader never sees an empty FAQ tab — but locally the author sees it to write.
   *
   * Pro Tips is the exception: content-gated EVERYWHERE, local dev included. Those pages are authored
   * per-screen through the agreed-list workflow (files written directly + manifest regen), not via the
   * in-drawer pencil — so an empty Pro Tips tab on every screen is noise, not an affordance.
   */
  readonly visibleTabs = computed<HelpTab[]>(() => {
    const component = this.component();
    if (!component) return [];
    const canAuthor = this.manifest.canEdit();
    return this.tabs.filter(
      (tab) =>
        this.manifest.has(`${component}/${tab.topic}`) ||
        (canAuthor && tab.topic !== 'pro-tips')
    );
  });

  /** True when the drawer is showing a page the user searched to, not the one they're standing on. */
  readonly isSearchedTo = computed(() => this.overrideComponent() !== null);

  /** The tab label for a topic key — reuses the same vocabulary the tab strip shows. */
  topicLabel(topic: string): string {
    return this.tabs.find((t) => t.topic === topic)?.label ?? topic;
  }

  open(): void {
    this.overrideComponent.set(null);
    this.query.set('');
    // No explicit browsing state needed: a route with no help has no component, and showManual()
    // already resolves that to the contents.
    this.browsing.set(false);
    this.isOpen.set(true);
    this.editing.set(false);
    this.activeTopic.set(this.visibleTabs()[0]?.topic ?? 'overview');
    this.load();
    this.loadIndex();

    // On a route with no help of its own, search is the only thing on offer — start the user in it.
    if (!this.hasPageHelp()) {
      afterNextRender(() => this.searchInput()?.nativeElement.focus(), { injector: this.injector });
    }
  }

  close(): void {
    this.isOpen.set(false);
    this.editing.set(false);
    this.query.set('');
    // Drop any searched-to topic, so the next "?" shows THIS page's help again.
    this.overrideComponent.set(null);
  }

  /** Fetch the search index once per session, on first drawer open. */
  private loadIndex(): void {
    if (this.docs().length > 0 || this.indexLoading()) return;
    this.indexLoading.set(true);
    this.search
      .load()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (docs) => {
          this.docs.set(docs);
          this.indexLoading.set(false);
        },
        error: () => this.indexLoading.set(false),
      });
  }

  onQuery(value: string): void {
    this.query.set(value);
    this.searchActiveIndex.set(0);
  }

  clearQuery(): void {
    this.query.set('');
    this.searchActiveIndex.set(0);
    this.searchInput()?.nativeElement.focus();
  }

  /** Show any page's help in this drawer — the landing point for both results and the contents. */
  openPage(component: string, topic: string): void {
    this.overrideComponent.set(component);
    this.activeTopic.set(topic);
    this.query.set('');
    this.browsing.set(false);
    this.editing.set(false);
    this.load();
  }

  /** Open the table of contents. Reachable from every page, which is what unifies the two views. */
  showAllTopics(): void {
    this.query.set('');
    this.searchActiveIndex.set(0);
    this.browsing.set(true);
    this.editing.set(false);
  }

  /** Leave the manual and go back to reading whichever page the drawer was showing. */
  backToPageContent(): void {
    this.query.set('');
    this.browsing.set(false);
  }

  chooseResult(hit: HelpSearchHit): void {
    this.openPage(hit.component, hit.topic);
  }

  /** Leave a searched-to page and return to the help for the route the user is actually on. */
  backToPage(): void {
    this.overrideComponent.set(null);
    this.browsing.set(false);
    this.query.set('');
    this.activeTopic.set(this.visibleTabs()[0]?.topic ?? 'overview');
    this.load();
  }

  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      // Escape unwinds one level at a time: query → the manual → the drawer. Never two at once.
      if (this.query()) this.clearQuery();
      else if (this.browsing() && this.component()) this.backToPageContent();
      else this.close();
      return;
    }

    const results = this.results();
    if (!results.length) return;

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.searchActiveIndex.set((this.searchActiveIndex() + 1) % results.length);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.searchActiveIndex.set(
        (this.searchActiveIndex() - 1 + results.length) % results.length
      );
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const hit = results[this.searchActiveIndex()];
      if (hit) this.chooseResult(hit);
    }
  }

  selectTab(topic: string): void {
    if (topic === this.activeTopic()) return;
    this.activeTopic.set(topic);
    this.editing.set(false);
    this.load();
  }

  private load(): void {
    const component = this.component();
    if (!component) {
      this.content.set(null);
      this.failed.set(false);
      this.loading.set(false);
      return;
    }

    const topic = this.activeTopic();
    const key = `${component}/${topic}`;
    const cached = this.cache.get(key);
    if (cached) {
      this.content.set(cached);
      this.failed.set(false);
      this.loading.set(false);
      return;
    }

    // Gate on the manifest: only fetch topics that actually have a file. A GET for a missing static
    // asset would fall through to the SPA's index.html (200 + HTML), not a 404 — so never request one.
    if (!this.manifest.has(key)) {
      this.content.set({ component, topic, html: '', exists: false });
      this.failed.set(false);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.failed.set(false);
    this.help.getContent(component, topic).subscribe({
      next: (c) => {
        this.cache.set(key, c);
        this.content.set(c);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  /** Enter edit mode. The lazily-loaded <app-help-editor> seeds itself from the active tab's content. */
  startEdit(): void {
    this.editing.set(true);
  }

  cancelEdit(): void {
    this.editing.set(false);
  }

  /**
   * Persist the HTML the editor emits (already collapsed for overview / serialized for FAQ) to the
   * working tree, update the cache + read view, and leave edit mode.
   */
  onEditorSave(html: string): void {
    const component = this.component();
    if (!component) return;
    const topic = this.activeTopic();
    const key = `${component}/${topic}`;

    this.saving.set(true);
    this.help
      .saveContent(component, topic, html)
      .then(() => {
        const saved: HelpContent = { component, topic, html, exists: true };
        this.cache.set(key, saved);
        this.content.set(saved);
        this.editing.set(false);
        this.saving.set(false);
        this.manifest.markAvailable(key);
        this.toast.show('Saved to your working tree — commit & push to publish', 'success');
      })
      .catch((err: unknown) => {
        this.saving.set(false);
        const e = err as { name?: string; message?: string };
        const msg =
          e?.name === 'AbortError' ? 'Save cancelled' : e?.message ?? 'Failed to save help content';
        this.toast.show(msg, 'danger');
      });
  }
}
