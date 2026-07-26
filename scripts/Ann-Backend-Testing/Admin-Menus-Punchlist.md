# Admin Menus Punchlist

Ann's review of **Director / SuperUser menu functions** (and other admin-side items that surface during it). Sibling to `Payment-Test-Punchlist.md`, which stays scoped to registration/payment.

- **Item IDs:** `AM-###`, numbered oldest → newest (newest at the bottom). The `AM-` prefix keeps this queue distinct from the payment doc's `PL-` — Todd greps `AM-` for admin, `PL-` for payment, no number collisions.
- **Each item** carries a Claude-verified root cause (code + DB where relevant) and clear **For Todd** directions.
- **Cross-topic:** if an admin finding actually belongs to another area, it still lives here (with a topic tag) so Todd sees it in this review.

### Marker legend (same as the payment punchlist)
- `✅ VERIFIED PASSING (Ann, MM-DD)` — Ann retested a fix, confirmed good
- `🟡 FIXED (Todd, MM-DD) — awaiting Ann verify` — fix shipped, queued for Ann's next-pass retest
- `🔴 STILL OPEN` / `🔴 RE-OPENED` — not yet fixed / regressed
- `✅ Acknowledged (Ann, MM-DD)` — Ann signed off on a decision (Won't Fix / by-design), no code change
- `Won't Fix` — decision recorded with rationale
- `🔵 REVISIT` — parked feature/enhancement, circle back

---

### AM-001: [Communications / Bulletins] The bulletin rich-text editor exposes far fewer options than Legacy — enrich the Syncfusion toolbar to match (within brand guardrails)
- **Topic**: Communications → Bulletins editor
- **Tested**: Bulletins → create/edit bulletin → the rich-text editor
- **Request (Ann)**: The bulletin editor has **far fewer formatting options than the Legacy bulletin editor**. Can Syncfusion meet the function we had previously? — and if so, enrich it.
- **Answer (verified): Yes — comfortably. The limit is *configuration*, not a Syncfusion capability ceiling.**
  - Every RTE in the app (bulletins, job-config HTML fields, the help editor) shares **one deliberately-minimal toolbar** config, `JOB_CONFIG_RTE_TOOLS` ([rte-config.ts:2-9](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/shared/rte-config.ts#L2)), exposing only **10 tools**: Bold · Italic · Underline · FontColor · BackgroundColor · OrderedList · UnorderedList · CreateLink · Undo · Redo. No font family/size, headings, alignment, indent, tables, images, or source view. Consumed by [bulletin-form-modal.component.ts:400](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/communications/bulletins/components/bulletin-form-modal.component.ts#L400) (and the help editor, job-config tabs).
  - **Syncfusion RichTextEditor supports far more, all built-in** (just not enabled): **Text** — FontName, FontSize, Formats (Paragraph / H1–H6 / Quote / Code), StrikeThrough, SuperScript, SubScript, ClearFormat; **Layout** — Alignments (L/C/R/Justify), Outdent/Indent, HorizontalLine; **Insert** — CreateTable (Table module), Image, Audio/Video, EmojiPicker; **Power-user** — SourceCode (edit raw HTML), FullScreen, Print, PasteCleanup. This matches or exceeds the Legacy editor.
- **Severity**: Feature / Legacy-parity gap
- **Status**: Open (Ann, 2026-07-26)
- **The real decision is a product/brand one, not technical**: the minimal toolbar was almost certainly a **deliberate brand-safety choice** — fewer fonts/colors/sizes means a director can't produce off-brand content. This is the exact tension **PL-043-T** (payment punchlist) already flags: *"more expressive range (tables, sectioning) without regaining the ability to break brand."* So the question for Todd is **which tools to allow back in**, and how to **constrain styles** so the added power doesn't reopen brand-break risk.
- **For Todd — the change**:
  1. **Expand the toolbar** — add the wanted tools to the `items` list and enable the needed modules (e.g. **Table**, **Image**). Reaching Legacy parity is a config change; no new dependency.
  2. **Constrain, don't just open** — pair it with **PasteCleanup** + an allowed formats/colors policy so directors gain tables/sectioning without off-brand fonts/colors (the PL-043-T guardrail).
  3. **Shared-config caution** — `JOB_CONFIG_RTE_TOOLS` is shared across **bulletins, job-config HTML fields, and the help editor**, so widening it hits all three. Decide whether to **enrich the shared config** or give **bulletins its own richer config** (leaving job-config/help minimal).
  - **Cross-ref**: PL-057 #5 (payment punchlist) asks to add an RTE to the **ARB defensive email** (today a bare textarea); it should **reuse whatever toolbar config lands here** so the editing experience is consistent.
