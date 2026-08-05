/**
 * Lifts body-parented Syncfusion popups into the top layer while a modal dialog is open.
 *
 * ## The bug this exists for
 *
 * `tsic-dialog` opens via `dialog.showModal()`, which promotes the <dialog> into the
 * browser's **top layer**. Top-layer elements paint above the entire normal stacking
 * context — z-index is not part of that comparison at all.
 *
 * Several Syncfusion controls append their popup to `document.body` rather than to their
 * own element (`ej2-splitbuttons/drop-down-button.js` → `appendToElement = document.body`).
 * A body-parented popup therefore paints UNDER the dialog, always, and no z-index value
 * can rescue it. Measured in the bulletin editor: the Font Size popup was open, sized
 * 96×162, correctly positioned, `visibility: visible`, z-index 1015 — and invisible.
 *
 * Confirmed affected in the RTE toolbar: **Font Size, Font Colour, Background Colour**
 * (all `DropDownButton`). Confirmed NOT affected: the table quick-toolbar and the link
 * dialog, both of which Syncfusion parents inside the editor element.
 *
 * ## Why promotion rather than re-parenting
 *
 * Moving the popup into the dialog would fix the layer but break the position: ej2
 * computes absolute coordinates against `document.body`, so changing the offset parent
 * invalidates them. It would also subject the popup to `.modal-content`'s `overflow:auto`
 * clipping. Promoting via the Popover API leaves the element exactly where it is — same
 * parent, same coordinates — and only changes which layer it paints in. Nothing about
 * Syncfusion's positioning is touched.
 *
 * ## Why this is anchored to the dialog, not to the editor
 *
 * The colour pickers' popup ids (`e-split-btn_1_dropdownbtn-popup`) come from a global
 * counter and carry no reference to the editor that owns them, so "find the popups
 * belonging to this RTE" is not reliably derivable. Anchoring to the dialog sidesteps
 * ownership entirely — and it covers any other body-parented ej2 popup (dropdownlist,
 * date picker, multiselect) dropped into a modal later, which would otherwise hit this
 * same wall with its own bespoke fix.
 *
 * The real structural fix is for `tsic-dialog` to stop using `showModal()` and supply its
 * own backdrop, removing the top layer from the picture. That is a change to every modal
 * in the app and is deliberately deferred; this is the contained version.
 */

/** Body-level popups ej2 creates outside the component. Matches the observed markup. */
const POPUP_SELECTOR = '.e-popup, [id$="-popup"]';

/** True once we know the browser can do this at all. Old browsers degrade to the status quo. */
const SUPPORTED = typeof HTMLElement !== 'undefined'
  && typeof (HTMLElement.prototype as { showPopover?: unknown }).showPopover === 'function';

function isVisible(el: HTMLElement): boolean {
  return getComputedStyle(el).display !== 'none' && el.getBoundingClientRect().width > 0;
}

function promote(el: HTMLElement, dialog: HTMLElement): void {
  // Already inside the dialog (table quick-toolbar, link dialog) — nothing to fix, and
  // promoting it would pointlessly re-layer an element that is already correct.
  if (dialog.contains(el)) return;

  if (!el.hasAttribute('popover')) {
    // "manual", never "auto": auto brings light-dismiss and popover-stack semantics that
    // would fight Syncfusion, which already owns this popup's open/close lifecycle.
    el.setAttribute('popover', 'manual');
  }
  if (!el.matches(':popover-open')) {
    try { el.showPopover(); } catch { /* already shown, or detached mid-flight */ }
  }
}

function demote(el: HTMLElement): void {
  if (el.matches(':popover-open')) {
    try { el.hidePopover(); } catch { /* detached mid-flight */ }
  }
}

/**
 * Start watching. Returns a disposer — call it when the dialog closes.
 *
 * Cost is deliberately bounded: one `childList` observer on <body>'s direct children, plus
 * one attribute observer per popup element filtered to `style`/`class`. No subtree-wide
 * attribute observation, which would fire on every style change in the app.
 */
export function watchTopLayerPopups(dialog: HTMLElement): () => void {
  if (!SUPPORTED) return () => { /* no-op: pre-Popover-API browser */ };

  const watched = new WeakSet<HTMLElement>();
  const observers: MutationObserver[] = [];

  const sync = (el: HTMLElement) => {
    if (isVisible(el)) promote(el, dialog); else demote(el);
  };

  const watch = (el: HTMLElement) => {
    if (watched.has(el)) return;
    watched.add(el);
    const obs = new MutationObserver(() => sync(el));
    obs.observe(el, { attributes: true, attributeFilter: ['style', 'class'] });
    observers.push(obs);
    // ej2 creates these eagerly and toggles display, so one may already be open.
    sync(el);
  };

  const scan = () => {
    document.body.querySelectorAll<HTMLElement>(POPUP_SELECTOR).forEach(el => {
      if (!dialog.contains(el)) watch(el);
    });
  };

  scan();

  // Popups created after the dialog opened (a lazily-rendered control, a second editor).
  const bodyObs = new MutationObserver(scan);
  bodyObs.observe(document.body, { childList: true });
  observers.push(bodyObs);

  return () => {
    observers.forEach(o => o.disconnect());
    // Leave nothing in the top layer behind us — a stale entry would outlive the dialog.
    document.body.querySelectorAll<HTMLElement>(POPUP_SELECTOR).forEach(demote);
  };
}
