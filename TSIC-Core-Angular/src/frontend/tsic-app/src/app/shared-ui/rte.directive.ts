import { Directive, inject } from '@angular/core';
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
export class TsicRteDirective {
  private readonly rte = inject(RichTextEditorComponent, { self: true });

  constructor() {
    this.rte.toolbarSettings = TSIC_RTE_TOOLS;
    this.rte.fontSize = TSIC_RTE_FONT_SIZES;
  }
}
