/**
 * Canonical Rich Text Editor toolbar config — THE toolbar for every RTE in the app.
 *
 * Every `ejs-richtexteditor` binds `[toolbarSettings]` to this. Do not hand-roll a
 * local `items:` array in a component: three surfaces had drifted into byte-identical
 * copies of this list before it was consolidated here, which is how one of them ends
 * up a tool behind the others.
 *
 * Deliberately absent: `FontName` / `FontSize`. Directors get emphasis, colour, lists
 * and structure; they do not get an uncontrolled font picker, because bulletin and
 * confirmation copy has to stay on-brand (AM-001, reaffirmed AM-045).
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
    'FontColor', 'BackgroundColor', '|',
    'OrderedList', 'UnorderedList', '|',
    'CreateTable', '|',
    'CreateLink', '|', 'Undo', 'Redo',
  ],
};
