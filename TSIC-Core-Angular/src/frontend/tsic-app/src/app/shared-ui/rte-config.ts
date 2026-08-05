/**
 * Canonical Rich Text Editor configuration — THE settings for every RTE in the app.
 *
 * Applied by the `tsicRte` directive (rte.directive.ts), not by hand. Do not hand-roll
 * a local `items:` array in a component: three surfaces had drifted into byte-identical
 * copies of the toolbar before it was consolidated here, which is how one of them ends
 * up a tool behind the others.
 *
 * `CreateTable` carries more than the insert button: mounting it also turns on
 * Syncfusion's table quick-toolbar, which is where the row/column CRUD actually lives
 * (`TableRows` / `TableColumns` dropdowns — insert above/below, insert left/right,
 * delete row, delete column — plus `TableRemove`). Those are library defaults, so no
 * custom icons and no reaching into the editor-manager's private table methods.
 *
 * Requires the host component to import `RichTextEditorAllModule` (not the bare
 * `RichTextEditorModule`) — the table + quick-toolbar services ship with the All
 * variant, and without them `CreateTable` renders as a dead button.
 */
export const TSIC_RTE_TOOLS = {
  items: [
    'Bold', 'Italic', 'Underline', '|',
    'FontColor', 'BackgroundColor', 'FontSize', '|',
    'OrderedList', 'UnorderedList', '|',
    'CreateTable', '|',
    'CreateLink', '|', 'Undo', 'Redo',
  ],
};

/**
 * The curated font-size list — the second half of shipping `FontSize`, not optional polish.
 *
 * Syncfusion's stock list (`ej2-richtexteditor/src/models/items.js`) is
 * `Default · 8pt · 10pt · 12pt · 14pt · 18pt · 24pt · 36pt`, and it applies automatically
 * to any editor that mounts the `FontSize` toolbar item without supplying its own list.
 * Adding the button alone would therefore hand every director a 36pt option nobody chose.
 *
 * So: four sizes, labelled by intent rather than by number. A director picking "Large"
 * cannot produce a billboard; a director picking "24 pt" from a list of eight eventually
 * does. Constrain the vocabulary, not the user — the same principle as the palette.
 *
 * `Default` (empty value) clears back to the inherited size, so a size choice is undoable
 * without guessing which entry was the original.
 *
 * Values are `px`, not Syncfusion's `pt`: these land as inline styles on the element
 * (`<span style="font-size:20px">`) and travel into email bodies, and `px` is the more
 * reliably honoured unit across mail clients. CSS custom properties (`var(--font-size-lg)`)
 * would be the DRY-purist choice and are deliberately NOT used — they don't resolve in most
 * mail clients and can be stripped, which would break exactly the surface that matters most.
 *
 * SCOPE CEILING (Todd, 2026-08-05): font **size** is the full extent of the accommodation.
 * `FontName` is never added — an uncontrolled font picker dilutes the brand, and that part
 * of AM-001 stands unamended. Do not read this entry as an opening to grow the toolbar.
 */
export const TSIC_RTE_FONT_SIZES = {
  width: '72px',
  items: [
    { text: 'Default', value: '' },
    { text: 'Small', value: '12px' },
    { text: 'Normal', value: '16px' },
    { text: 'Large', value: '20px' },
    { text: 'Extra Large', value: '24px' },
  ],
};
