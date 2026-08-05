import { AfterViewInit, Directive, ElementRef, OnDestroy, inject } from '@angular/core';
import { RichTextEditorComponent } from '@syncfusion/ej2-angular-richtexteditor';
import { TSIC_RTE_TOOLS, TSIC_RTE_FONT_SIZES } from './rte-config';

/**
 * Applies the app-wide RTE configuration to a Syncfusion editor.
 *
 *     <ejs-richtexteditor tsicRte [(value)]="body" ...>
 *
 * Why a directive rather than per-component bindings: Syncfusion splits the editor's
 * configuration across *separate inputs* — `toolbarSettings` mounts the buttons,
 * `fontSize` decides what the size dropdown contains. Bound by hand, those two must be
 * remembered together on all eight editors, and forgetting the second one silently
 * inherits Syncfusion's stock 8pt–36pt list. That is the same drift that produced three
 * byte-identical copies of the toolbar array before it was consolidated.
 *
 * With this directive there is one place to change, and settings cannot arrive
 * half-applied on one surface.
 *
 * Assigned in the constructor, deliberately, NOT in `ngOnInit`: the ej2 component reads its
 * own inputs during its lifecycle hooks, and directive/component hook ordering on a shared
 * element is not a guarantee worth betting a silently-empty toolbar on. A constructor runs
 * before every `ngOnInit` on the element, so the settings are always in place first.
 *
 * The host component still imports `RichTextEditorAllModule`; `CreateTable` needs the
 * table + quick-toolbar services that only the All variant provides.
 */
@Directive({
  selector: 'ejs-richtexteditor[tsicRte]',
  standalone: true,
})
export class TsicRteDirective implements AfterViewInit, OnDestroy {
  private readonly rte = inject(RichTextEditorComponent, { self: true });
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  constructor() {
    this.rte.toolbarSettings = TSIC_RTE_TOOLS;
    this.rte.fontSize = TSIC_RTE_FONT_SIZES;
  }

  /**
   * Closes the two file-ingestion routes that no Syncfusion setting can turn off.
   *
   * `Image` is link-only (see `rte-config.ts`): we never host an uploaded file. The
   * dialog's Browse button is hidden in CSS, but dropping a file onto the editor and
   * pasting a screenshot both bypass the dialog entirely — the first produces a `blob:`
   * URL that dies on refresh, the second inlines base64 into the database column.
   *
   * Bound in the **capture** phase on the editor's root, so these run before the
   * handlers Syncfusion binds to the inner contenteditable and can stop the event
   * reaching them. Bubble-phase listeners would fire after the image was already
   * inserted. (Safe because the editor is not in iframe mode — `iframeSettings.enable`
   * is false, so the content area is in this document and shares our event path.)
   */
  ngAfterViewInit(): void {
    const el = this.host.nativeElement;
    el.addEventListener('drop', this.blockFileDrop, true);
    el.addEventListener('paste', this.blockFilePaste, true);
  }

  ngOnDestroy(): void {
    const el = this.host.nativeElement;
    el.removeEventListener('drop', this.blockFileDrop, true);
    el.removeEventListener('paste', this.blockFilePaste, true);
  }

  /**
   * Arrow properties, not methods: `removeEventListener` matches on function identity,
   * and a bound method produces a new reference per call — the listener would survive
   * the directive and keep a reference to a destroyed component.
   */
  private readonly blockFileDrop = (event: DragEvent): void => {
    if (!event.dataTransfer?.files.length) return; // dragging text or an existing image within the editor
    event.preventDefault();
    event.stopPropagation();
  };

  private readonly blockFilePaste = (event: ClipboardEvent): void => {
    const clipboard = event.clipboardData;
    if (!clipboard) return;

    const hasImageFile = Array.from(clipboard.files).some(file => file.type.startsWith('image/'));
    if (!hasImageFile) return;

    // A copy from a web page carries both an image file and the source markup. That
    // markup is what we want — it holds `<img src="https://…">`, a link to an image on
    // someone else's server, which is exactly the supported case. Only block when the
    // file is all there is, i.e. a screenshot or a file-manager copy.
    if (clipboard.getData('text/html')) return;

    event.preventDefault();
    event.stopPropagation();
  };
}
