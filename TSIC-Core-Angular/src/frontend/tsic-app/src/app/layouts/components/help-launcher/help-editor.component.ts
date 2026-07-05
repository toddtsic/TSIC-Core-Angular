import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  RichTextEditorAllModule,
  RichTextEditorComponent,
} from '@syncfusion/ej2-angular-richtexteditor';
import {
  CdkDropList,
  CdkDrag,
  CdkDragHandle,
  type CdkDragDrop,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { JOB_CONFIG_RTE_TOOLS } from '../../../views/configure/job/shared/rte-config';

/**
 * One FAQ entry while editing the FAQ tab as a structured list (not raw HTML). Parsed from the stored
 * <details class="faq-item"> blocks on open, reordered by drag, and serialized back to that HTML on save.
 */
interface FaqEditItem {
  id: string;
  question: string;
  answerHtml: string;
}

/**
 * A single FAQ accordion block. Shared by the FAQ tab's "Add question" button and the Insert palette's
 * "Question & answer" entry so both drop the identical, style-sheet-correct placeholder.
 */
interface HelpSnippet {
  readonly label: string;
  readonly icon: string;
  readonly html: string;
}

const FAQ_ITEM_HTML = `<details class="faq-item" open>
  <summary><i class="bi bi-chevron-right faq-caret" aria-hidden="true"></i><span>Replace with your question</span></summary>
  <div class="faq-a">
    <p>Replace with the answer. A sentence or two is ideal; <strong>bold</strong> the key term so it stands out.</p>
  </div>
</details>`;

/**
 * The "Insert" palette — one entry per building block in the manual's style sheet. Each drops a
 * pre-styled placeholder into the editor so authoring is just replacing the dummy text. Snippets are
 * class-only (styles/_help-content.scss owns the look), so they render identically here and on the page,
 * and a manual-wide restyle is one edit to that partial — never a find-and-replace across content files.
 */
const HELP_SNIPPETS: readonly HelpSnippet[] = [
  {
    label: 'Question & answer',
    icon: 'bi-chat-left-text',
    html: FAQ_ITEM_HTML,
  },
  {
    label: 'Warning callout',
    icon: 'bi-exclamation-triangle',
    html: `<div class="help-callout help-callout--warning">
  <i class="bi bi-exclamation-triangle-fill" aria-hidden="true"></i>
  <div>
    <strong>Replace with the key warning.</strong>
    <p class="help-callout__body">Add a supporting sentence here.</p>
  </div>
</div>`,
  },
  {
    label: 'Tip callout',
    icon: 'bi-lightbulb',
    html: `<div class="help-callout help-callout--tip">
  <i class="bi bi-lightbulb" aria-hidden="true"></i>
  <div><strong>Tip:</strong> replace with your tip.</div>
</div>`,
  },
  {
    label: 'Success callout',
    icon: 'bi-shield-check',
    html: `<div class="help-callout help-callout--success">
  <i class="bi bi-shield-check" aria-hidden="true"></i>
  <div>
    <strong>Replace with the reassuring point.</strong>
    <p class="help-callout__body">Add a supporting sentence here.</p>
  </div>
</div>`,
  },
  {
    label: 'Status badges',
    icon: 'bi-tag',
    html: `<div class="help-badge-row">
  <span class="badge rounded-pill text-bg-secondary">Label one</span>
  <span class="badge rounded-pill text-bg-success"><i class="bi bi-check-lg"></i> Label two</span>
  <span class="badge rounded-pill text-bg-warning">WL</span>
</div>`,
  },
  {
    label: 'Two-column cards',
    icon: 'bi-layout-split',
    html: `<div class="help-card-grid">
  <div class="help-card">
    <span class="badge rounded-pill text-bg-secondary">First state</span>
    <p>Describe what this state means.</p>
  </div>
  <div class="help-card help-card--success">
    <span class="badge rounded-pill text-bg-success"><i class="bi bi-check-lg"></i> Second state</span>
    <p>Describe the contrasting state.</p>
  </div>
</div>`,
  },
  {
    label: 'Button example',
    icon: 'bi-hand-index',
    html: `<p><button type="button" class="btn btn-primary btn-sm" disabled>Button label</button></p>`,
  },
];

/**
 * The help drawer's authoring UI — Syncfusion RichTextEditor, structured FAQ editor, and the Insert
 * palette. Extracted from HelpLauncherComponent so the (heavy) RTE lives in a lazily-loaded chunk:
 * the launcher renders <app-help-editor> only inside a @defer block, and editing is gated to local
 * development, so this code never ships in the initial bundle and is never fetched in staging/prod.
 *
 * The launcher owns persistence: this component seeds itself from [topic]/[html], and on save emits the
 * finished HTML (accordions collapsed for overview, <details> blocks serialized for FAQ) via (save).
 */
@Component({
  selector: 'app-help-editor',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RichTextEditorAllModule,
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './help-editor.component.html',
  styleUrl: './help-editor.component.scss',
})
export class HelpEditorComponent implements OnInit {
  /** 'faq' for the structured Q&A editor, anything else for the raw-HTML overview editor. */
  readonly topic = input.required<string>();
  /** The stored HTML for the active tab, used to seed the editor once on init. */
  readonly html = input.required<string>();
  /** True while the launcher is persisting a save, to disable the action buttons. */
  readonly saving = input(false);

  /** The finished HTML for the active tab, ready to persist. */
  readonly save = output<string>();
  /** Discard edits and leave edit mode. */
  readonly cancelEdit = output<void>();

  readonly rteEditor = viewChild<RichTextEditorComponent>('rteEditor');
  /** The answer editor for the currently-expanded FAQ card (only one renders at a time). */
  readonly faqRte = viewChild<RichTextEditorComponent>('faqRte');
  readonly rteTools = JOB_CONFIG_RTE_TOOLS;

  /** The style-sheet building blocks offered by the "Insert" dropdown while editing. */
  readonly snippets = HELP_SNIPPETS;

  readonly draft = signal('');
  readonly insertOpen = signal(false);

  // FAQ tab is edited as a structured, drag-orderable list of Q&A items rather than raw HTML.
  readonly faqItems = signal<FaqEditItem[]>([]);
  readonly activeFaqId = signal<string | null>(null);

  ngOnInit(): void {
    // FAQ tab: edit as a structured, drag-orderable list. Parse the stored <details> blocks into items.
    if (this.topic() === 'faq') {
      const items = this.parseFaqItems(this.html());
      this.faqItems.set(items);
      // Expand the single item so its answer editor is ready; otherwise start with all collapsed.
      this.activeFaqId.set(items.length === 1 ? items[0].id : null);
      return;
    }

    // Overview/Help tab: edit the raw HTML in the RTE. Expand every accordion so its answer is visible/
    // editable (collapsed <details> hide their body). The `open` state is authoring-only — save() strips it.
    this.draft.set(this.expandAccordions(this.html()));
  }

  onSave(): void {
    let html: string;
    if (this.topic() === 'faq') {
      this.flushActiveFaq();
      html = this.serializeFaqItems(this.faqItems());
    } else {
      html = this.collapseAccordions(this.draft());
    }
    this.save.emit(html);
  }

  onCancel(): void {
    this.cancelEdit.emit();
  }

  toggleInsert(): void {
    this.insertOpen.update((open) => !open);
  }

  /** Add `open` to any FAQ <details> that lacks it, so authors can see the answer while editing. */
  private expandAccordions(html: string): string {
    return html.replace(/<details\b(?![^>]*\bopen\b)([^>]*)>/gi, '<details$1 open>');
  }

  /** Strip the authoring-only `open` attribute so saved content renders collapsed on the read view. */
  private collapseAccordions(html: string): string {
    return html.replace(/(<details\b[^>]*?)\s+open(\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?/gi, '$1');
  }

  // ── Structured FAQ editing (FAQ tab) ──────────────────────────────────────────────────────────

  /** Append a blank question card and open it for editing. */
  addFaqItem(): void {
    this.flushActiveFaq();
    const item: FaqEditItem = { id: this.newId(), question: '', answerHtml: '' };
    this.faqItems.set([...this.faqItems(), item]);
    this.activeFaqId.set(item.id);
  }

  /** Expand a card to edit its answer (collapsing whichever was open), or collapse it if already open. */
  toggleFaq(id: string): void {
    this.flushActiveFaq();
    this.activeFaqId.set(this.activeFaqId() === id ? null : id);
  }

  updateQuestion(id: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.faqItems.set(
      this.faqItems().map((it) => (it.id === id ? { ...it, question: value } : it))
    );
  }

  updateAnswer(id: string, event: { value?: string }): void {
    const value = event.value ?? '';
    this.faqItems.set(
      this.faqItems().map((it) => (it.id === id ? { ...it, answerHtml: value } : it))
    );
  }

  removeFaq(id: string): void {
    this.faqItems.set(this.faqItems().filter((it) => it.id !== id));
    if (this.activeFaqId() === id) this.activeFaqId.set(null);
  }

  dropFaq(event: CdkDragDrop<FaqEditItem[]>): void {
    const items = [...this.faqItems()];
    moveItemInArray(items, event.previousIndex, event.currentIndex);
    this.faqItems.set(items);
  }

  /**
   * Copy the live answer editor's value back into the active item before we collapse it, reorder, add, or
   * save — the RTE's (change) only fires on blur, so an in-progress answer would otherwise be lost.
   */
  private flushActiveFaq(): void {
    const id = this.activeFaqId();
    if (!id) return;
    const rte = this.faqRte();
    if (!rte) return;
    const value = rte.value ?? '';
    this.faqItems.set(
      this.faqItems().map((it) => (it.id === id ? { ...it, answerHtml: value } : it))
    );
  }

  /** Parse stored FAQ HTML (<details class="faq-item">…) into editable items. */
  private parseFaqItems(html: string): FaqEditItem[] {
    if (!html.trim()) return [];
    const doc = new DOMParser().parseFromString(html, 'text/html');
    return Array.from(doc.querySelectorAll('details.faq-item')).map((el) => ({
      id: this.newId(),
      question: (el.querySelector('summary span') ?? el.querySelector('summary'))?.textContent?.trim() ?? '',
      answerHtml: el.querySelector('.faq-a')?.innerHTML.trim() ?? '',
    }));
  }

  /** Serialize the item list back to the canonical <details class="faq-item"> blocks the read view renders. */
  private serializeFaqItems(items: FaqEditItem[]): string {
    return items
      .filter((it) => it.question.trim() || it.answerHtml.trim())
      .map((it) => {
        const q = this.escapeHtml(it.question.trim());
        const a = it.answerHtml.trim() || '<p></p>';
        return (
          `<details class="faq-item">\n` +
          `  <summary><i class="bi bi-chevron-right faq-caret" aria-hidden="true"></i><span>${q}</span></summary>\n` +
          `  <div class="faq-a">\n    ${a}\n  </div>\n` +
          `</details>`
        );
      })
      .join('\n');
  }

  private escapeHtml(text: string): string {
    return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  private newId(): string {
    return crypto.randomUUID();
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
}
