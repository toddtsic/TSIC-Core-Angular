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

/**
 * The single, app-wide "?" launcher. It reads the current route's help key (via HelpContextService)
 * and opens a right-side drawer with the authored HTML for that page — rendered with the app's own
 * design-system styles, so illustrations look like the real product. When no content exists yet, it
 * shows an "Under Development" state; a SuperUser on a sandbox environment sees a pencil that turns
 * the drawer into a Syncfusion editor and writes the working-tree file (Model A).
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

  readonly isOpen = signal(false);
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly content = signal<HelpContentDto | null>(null);
  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly draft = signal('');

  /** True when this page declares no help key at all — nothing to fetch or author. */
  readonly noKey = computed(() => !this.context.helpKey());

  /** The authored body, trusted for render. Content is SuperUser-authored and git-reviewed before prod. */
  readonly safeHtml = computed<SafeHtml | null>(() => {
    const html = this.content()?.html;
    return html ? this.sanitizer.bypassSecurityTrustHtml(html) : null;
  });

  /** Show the edit affordance only where the server permits it (sandbox) AND the user is a SuperUser. */
  readonly canEdit = computed(
    () => !!this.content()?.canEdit && this.auth.isSuperuser() && !this.noKey()
  );

  /**
   * Whether to show the "?" at all. Hidden wherever there's no authored content for the current route —
   * for everyone, SuperUsers included. Every keyed page gets a first authoring pass before humans edit,
   * so there's no need to surface the "?" on empty pages. SuperUsers still get the pencil to edit pages
   * that DO have content.
   */
  readonly available = computed(() => {
    const raw = this.context.helpKey();
    if (!raw) return false;
    const { component, topic } = this.context.parseKey(raw);
    return this.manifest.has(`${component}/${topic}`);
  });

  /** A human label for the drawer subtitle, derived from the help key. */
  readonly pageLabel = computed(() => {
    const key = this.context.helpKey();
    if (!key) return null;
    const { component, topic } = this.context.parseKey(key);
    return topic === 'overview' ? component : `${component} · ${topic}`;
  });

  open(): void {
    this.isOpen.set(true);
    this.editing.set(false);
    this.load();
  }

  close(): void {
    this.isOpen.set(false);
    this.editing.set(false);
    this.draft.set('');
  }

  private load(): void {
    const key = this.context.helpKey();
    if (!key) {
      this.content.set(null);
      this.failed.set(false);
      this.loading.set(false);
      return;
    }

    const { component, topic } = this.context.parseKey(key);
    this.loading.set(true);
    this.failed.set(false);
    this.help.getContent(component, topic).subscribe({
      next: (dto) => {
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
  }

  cancelEdit(): void {
    this.editing.set(false);
    this.draft.set('');
  }

  onRteChange(event: { value?: string }): void {
    this.draft.set(event.value ?? '');
  }

  save(): void {
    const key = this.context.helpKey();
    if (!key) return;
    const { component, topic } = this.context.parseKey(key);

    this.saving.set(true);
    this.help.saveContent(component, topic, this.draft()).subscribe({
      next: (dto) => {
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
