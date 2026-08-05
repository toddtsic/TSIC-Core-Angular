/**
 * Moves body-parented Syncfusion popups inside a modal dialog while it is open.
 *
 * ## The bug this exists for
 *
 * `tsic-dialog` opens via `dialog.showModal()`. That does two things to everything
 * outside the dialog: it paints them **under** the dialog (the dialog joins the top
 * layer, which is not part of the z-index comparison), and it makes them **inert** —
 * non-interactive, not merely hidden.
 *
 * Several Syncfusion controls append their popup to `document.body` rather than to
 * their own element (`ej2-splitbuttons/drop-down-button.js` → `appendToElement =
 * document.body`). Inside a modal, such a popup is therefore invisible AND dead.
 *
 * Measured in the bulletin editor: the Font Size popup was open, sized 96×162,
 * correctly positioned, `visibility: visible`, z-index 1015 — and unreachable.
 *
 * Confirmed affected (all `DropDownButton`): **Font Size, Font Colour, Background
 * Colour**. Confirmed NOT affected, because Syncfusion parents them inside the editor:
 * the **table quick-toolbar** and the **link dialog**. Those are left alone.
 *
 * ## A dead end worth recording
 *
 * The first attempt promoted the popup into the top layer with the Popover API
 * (`popover="manual"` + `showPopover()`), leaving it parented to `<body>`. That fixed
 * the painting — the popups appeared correctly positioned over the dialog — and did
 * nothing for interactivity: a click listener bound directly to the popup never fired,
 * not even `mousedown`. **Inertness is decided by DOM ancestry, not by paint layer.**
 * Do not retry that approach.
 *
 * ## What this does instead
 *
 * On open, move the popup inside the `<dialog>` element. As a descendant it is both
 * painted with the dialog and interactive. On close, move it back to `<body>` so
 * Syncfusion always re-positions it in the context it computes against.
 *
 * Two details that matter:
 *
 * - **The offset correction.** ej2 writes `top`/`left` resolved against `<body>`. Once
 *   re-parented, those resolve against the dialog's box instead, so the popup would sit
 *   off by exactly the dialog's own offset. We measure the element's viewport rect
 *   *before* moving it and rewrite the coordinates relative to the dialog's rect. Using
 *   measured rects rather than parsing the inline style keeps this correct regardless of
 *   scroll position.
 *
 * - **Appended to the `<dialog>`, never to `.modal-content`.** That inner element sets
 *   `overflow: auto` and `max-height: 90vh`, which would clip a dropdown opened near the
 *   bottom of a tall modal.
 *
 * ## Why this is anchored to the dialog rather than to the editor
 *
 * The colour pickers' popup ids (`e-split-btn_1_dropdownbtn-popup`) come from a global
 * counter and carry no reference to the editor that owns them, so "find the popups
 * belonging to this RTE" is not reliably derivable. Anchoring to the dialog sidesteps
 * ownership — and covers any other body-parented ej2 popup (dropdownlist, date picker,
 * multiselect) dropped into a modal later, which would otherwise need its own fix.
 *
 * The structural answer is for `tsic-dialog` to stop using `showModal()` and supply its
 * own backdrop, removing both the top layer and the inertness from the picture. That
 * changes every modal in the app — deferred deliberately; this is the contained version.
 */

/** Body-level popups ej2 creates outside the component. Matches the observed markup. */
const POPUP_SELECTOR = '.e-popup, [id$="-popup"]';

function isVisible(el: HTMLElement): boolean {
  return getComputedStyle(el).display !== 'none' && el.getBoundingClientRect().width > 0;
}

/**
 * Start watching. Returns a disposer — call it when the dialog closes.
 *
 * Observation cost is deliberately bounded: one `childList` observer on `<body>`'s direct
 * children, plus one `style`/`class` attribute observer per popup element. No subtree-wide
 * attribute observation, which would fire on every style change in the app.
 */
export function watchTopLayerPopups(dialog: HTMLElement): () => void {
  const watched = new WeakSet<HTMLElement>();
  /** Popups we have relocated. Also what makes the move idempotent: our own style writes
   *  re-trigger the observer, and without this we would re-measure an already-moved
   *  element and walk it across the screen one dialog-offset at a time. */
  const moved = new Set<HTMLElement>();
  const observers: MutationObserver[] = [];

  const moveIn = (el: HTMLElement) => {
    if (moved.has(el)) return;
    const before = el.getBoundingClientRect();   // where ej2 correctly put it
    dialog.appendChild(el);
    const box = dialog.getBoundingClientRect();
    el.style.top = `${before.top - box.top}px`;
    el.style.left = `${before.left - box.left}px`;
    moved.add(el);
  };

  const moveOut = (el: HTMLElement) => {
    if (!moved.has(el)) return;
    moved.delete(el);
    document.body.appendChild(el);
    // Coordinates are left as-is: ej2 rewrites top/left on every open, so the stale
    // dialog-relative values never get a chance to be used.
  };

  const sync = (el: HTMLElement) => {
    if (isVisible(el)) moveIn(el); else moveOut(el);
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
    // Only body-level popups are broken. Anything Syncfusion already parents inside the
    // dialog (table quick-toolbar, link dialog) is correct and must not be touched.
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
    // Put everything back before the dialog is torn down, or the popups leave with it.
    [...moved].forEach(moveOut);
  };
}
