import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import {
  RichTextEditorAllModule,
  RichTextEditorComponent,
} from '@syncfusion/ej2-angular-richtexteditor';
import type { HelpContentDto } from '@core/api';
import { HelpService } from '@infrastructure/services/help.service';
import { HelpContextService } from '@infrastructure/services/help-context.service';
import { HelpManifestService } from '@infrastructure/services/help-manifest.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { JOB_CONFIG_RTE_TOOLS } from '../../../views/configure/job/shared/rte-config';

interface HelpTab {
  readonly topic: string;
  readonly label: string;
  readonly icon: string;
}

interface HelpSnippet {
  readonly label: string;
  readonly icon: string;
  readonly html: string;
}

/**
 * The "Insert" palette — one entry per building block in the manual's style sheet. Each drops a
 * pre-styled placeholder into the editor so authoring is just replacing the dummy text, and every
 * snippet matches the hand-authored pages (same inline styles, same CSS-var + hex fallbacks).
 */
const HELP_SNIPPETS: readonly HelpSnippet[] = [
  {
    label: 'Question & answer',
    icon: 'bi-chat-left-text',
    html: `<h4>New question — replace with your question</h4>
<p>Replace with the answer. A sentence or two is ideal; <strong>bold</strong> the key term so it stands out.</p>`,
  },
  {
    label: 'Warning callout',
    icon: 'bi-exclamation-triangle',
    html: `<div style="display:flex; gap:var(--space-3,.75rem); padding:var(--space-4,1rem); border:1px solid var(--bs-warning,#ffc107); border-left-width:4px; border-radius:var(--radius-md,8px); background:var(--bs-tertiary-bg,#f5f5f4); margin-bottom:var(--space-5,1.25rem);">
  <i class="bi bi-exclamation-triangle-fill" style="font-size:1.35rem; color:var(--bs-warning,#ffc107);" aria-hidden="true"></i>
  <div>
    <strong>Replace with the key warning.</strong>
    <div style="margin-top:var(--space-1,.25rem); color:var(--bs-secondary-color);">Add a supporting sentence here.</div>
  </div>
</div>`,
  },
  {
    label: 'Tip callout',
    icon: 'bi-lightbulb',
    html: `<div style="display:flex; gap:var(--space-3,.75rem); padding:var(--space-4,1rem); border:1px solid var(--bs-border-color); border-left:4px solid var(--bs-primary); border-radius:var(--radius-md,8px); background:var(--bs-tertiary-bg,#f5f5f4); margin:var(--space-4,1rem) 0;">
  <i class="bi bi-lightbulb" style="font-size:1.2rem; color:var(--bs-primary);" aria-hidden="true"></i>
  <div><strong>Tip:</strong> replace with your tip.</div>
</div>`,
  },
  {
    label: 'Status badges',
    icon: 'bi-tag',
    html: `<div style="display:flex; flex-wrap:wrap; gap:var(--space-3,.75rem); margin-bottom:var(--space-4,1rem);">
  <span class="badge rounded-pill text-bg-secondary">Label one</span>
  <span class="badge rounded-pill text-bg-success"><i class="bi bi-check-lg"></i> Label two</span>
  <span class="badge rounded-pill text-bg-warning">WL</span>
</div>`,
  },
  {
    label: 'Button example',
    icon: 'bi-hand-index',
    html: `<p style="margin-bottom:var(--space-4,1rem);">
  <button type="button" class="btn btn-primary btn-sm" disabled>Button label</button>
</p>`,
  },
];

/**
 * The single, app-wide "?" launcher. It reads the current route's help key (via HelpContextService)
 * and opens a right-side drawer with two tabs for that page: Help (the authored explainer) and FAQ
 * (a growing Q&A). Each tab is a topic under the same component — Help = "overview", FAQ = "faq" — so
 * the whole thing rides on the existing {component}/{topic} storage with no backend change.
 *
 * Content renders with the app's own design-system styles, so illustrations look like the real product.
 * A SuperUser on a sandbox environment sees a pencil that edits whichever tab is active via the
 * Syncfusion editor and writes the working-tree file (Model A). FAQ is the tab that grows over time.
 */
@Component({
  selector: 'app-help-launcher',
  standalone: true,
  imports: [CommonModule, FormsModule, RichTextEditorAllModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './help-launcher.component.html',
  styleUrl: './help-launcher.component.scss',
})
export class HelpLauncherComponent {
  private readonly help = inject(HelpService);
  private readonly context = inject(HelpContextService);
  private readonly manifest = inject(HelpManifestService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly sanitizer = inject(DomSanitizer);

  readonly rteEditor = viewChild<RichTextEditorComponent>('rteEditor');
  readonly rteTools = JOB_CONFIG_RTE_TOOLS;

  readonly tabs: readonly HelpTab[] = [
    { topic: 'overview', label: 'Help', icon: 'bi-life-preserver' },
    { topic: 'faq', label: 'FAQ', icon: 'bi-patch-question' },
  ];

  readonly isOpen = signal(false);
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly content = signal<HelpContentDto | null>(null);
  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly draft = signal('');
  readonly activeTopic = signal<string>('overview');
  readonly insertOpen = signal(false);

  /** The style-sheet building blocks offered by the "Insert" dropdown while editing. */
  readonly snippets = HELP_SNIPPETS;

  // Per-topic cache so switching Help <-> FAQ doesn't refetch/flicker within a session.
  private readonly cache = new Map<string, HelpContentDto>();

  /** The component (page) for the current route, or null when the route declares no help key. */
  readonly component = computed(() => {
    const raw = this.context.helpKey();
    return raw ? this.context.parseKey(raw).component : null;
  });

  /** The authored body, trusted for render. Content is SuperUser-authored and git-reviewed before prod. */
  readonly safeHtml = computed<SafeHtml | null>(() => {
    const html = this.content()?.html;
    return html ? this.sanitizer.bypassSecurityTrustHtml(html) : null;
  });

  /** Show the edit affordance only where the server permits it (sandbox) AND the user is a SuperUser. */
  readonly canEdit = computed(
    () => !!this.content()?.canEdit && this.auth.isSuperuser() && !!this.component()
  );

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
   * Which tabs to show: a tab appears when it has content, or when a SuperUser can author it (sandbox).
   * So regular users never see an empty FAQ tab — but a SuperUser on staging sees it and can write it.
   */
  readonly visibleTabs = computed<HelpTab[]>(() => {
    const component = this.component();
    if (!component) return [];
    const canAuthor = this.auth.isSuperuser() && this.manifest.canEdit();
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
    this.draft.set('');
  }

  selectTab(topic: string): void {
    if (topic === this.activeTopic()) return;
    this.activeTopic.set(topic);
    this.editing.set(false);
    this.draft.set('');
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
    const cached = this.cache.get(`${component}/${topic}`);
    if (cached) {
      this.content.set(cached);
      this.failed.set(false);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.failed.set(false);
    this.help.getContent(component, topic).subscribe({
      next: (dto) => {
        this.cache.set(`${component}/${topic}`, dto);
        this.content.set(dto);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  startEdit(): void {
    this.draft.set(this.content()?.html ?? '');
    this.editing.set(true);
    this.insertOpen.set(false);
  }

  cancelEdit(): void {
    this.editing.set(false);
    this.draft.set('');
    this.insertOpen.set(false);
  }

  toggleInsert(): void {
    this.insertOpen.update((open) => !open);
  }

  /** Append a style-sheet block to the end of the draft, then close the palette. */
  insertSnippet(html: string): void {
    this.insertOpen.set(false);
    const rte = this.rteEditor();
    const current = (rte?.value ?? this.draft() ?? '').trimEnd();
    const next = current ? `${current}\n${html}\n` : `${html}\n`;
    this.draft.set(next);
    if (rte) {
      rte.value = next;
    }
  }

  onRteChange(event: { value?: string }): void {
    this.draft.set(event.value ?? '');
  }

  save(): void {
    const component = this.component();
    if (!component) return;
    const topic = this.activeTopic();

    this.saving.set(true);
    this.help.saveContent(component, topic, this.draft()).subscribe({
      next: (dto) => {
        this.cache.set(`${component}/${topic}`, dto);
        this.content.set(dto);
        this.editing.set(false);
        this.saving.set(false);
        this.manifest.markAvailable(`${component}/${topic}`);
        this.toast.show('Help content saved', 'success');
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.show(err?.error?.message ?? 'Failed to save help content', 'danger');
      },
    });
  }
}
