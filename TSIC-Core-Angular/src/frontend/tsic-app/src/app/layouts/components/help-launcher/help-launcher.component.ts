import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
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
 * and opens a right-side drawer with two tabs for that page: Help (the authored explainer) and FAQ
 * (a growing Q&A). Each tab is a topic under the same component — Help = "overview", FAQ = "faq" —
 * served as a static asset from public/{component}/{topic}.html.
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

  readonly tabs: readonly HelpTab[] = [
    { topic: 'overview', label: 'Help', icon: 'bi-life-preserver' },
    { topic: 'faq', label: 'FAQ', icon: 'bi-patch-question' },
  ];

  readonly isOpen = signal(false);
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly content = signal<HelpContent | null>(null);
  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly activeTopic = signal<string>('overview');

  // Per-topic cache so switching Help <-> FAQ doesn't refetch/flicker within a session.
  private readonly cache = new Map<string, HelpContent>();

  /** The component (page) for the current route, or null when the route declares no help key. */
  readonly component = computed(() => {
    const raw = this.context.helpKey();
    return raw ? this.context.parseKey(raw).component : null;
  });

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
   * Whether to show the "?" at all. Hidden wherever the page has no content under any tab — for everyone,
   * SuperUsers included. Every keyed page gets a first authoring pass before humans edit.
   */
  readonly available = computed(() => {
    const component = this.component();
    return !!component && this.manifest.hasComponent(component);
  });

  /** A human label for the drawer subtitle, derived from the component key. */
  readonly pageLabel = computed(() => {
    const component = this.component();
    if (!component) return null;
    const spaced = component.replace(/-/g, ' ');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  });

  /**
   * Which tabs to show: a tab appears when it has content, or when it can be authored (local dev).
   * So on a deployed build a reader never sees an empty FAQ tab — but locally the author sees it to write.
   */
  readonly visibleTabs = computed<HelpTab[]>(() => {
    const component = this.component();
    if (!component) return [];
    const canAuthor = this.manifest.canEdit();
    return this.tabs.filter(
      (tab) => this.manifest.has(`${component}/${tab.topic}`) || canAuthor
    );
  });

  open(): void {
    this.isOpen.set(true);
    this.editing.set(false);
    this.activeTopic.set(this.visibleTabs()[0]?.topic ?? 'overview');
    this.load();
  }

  close(): void {
    this.isOpen.set(false);
    this.editing.set(false);
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
