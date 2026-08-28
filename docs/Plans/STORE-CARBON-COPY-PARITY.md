# Store — Carbon-Copy Parity Ledger

> **THE RULING (Todd, 2026-08-27): `reference/TSIC-Unify-2024` IS THE SPEC.**
> Where the new store diverges, legacy is right and the new code conforms — not the reverse.
> Ignore what is already built here. Ignore prior assumptions. **Every pathway migrates.**
> End state is carbon-copy functionality.
>
> **`migration-plans/018-merch-store.md` is retired.** Its status tables have been caught stating
> the opposite of the code, and its "RESOLVED" open questions were assumptions, not rulings.
> Do not cite it.
>
> **Never describe a legacy pathway from this file.** Open the legacy source. This file tracks
> *what still has to happen*, not *what legacy does* — a summary of legacy is a second source
> that rots.

## How to use this file

The unit is a **pathway**, not a feature: every controller action, grid column, toolbar button,
dialog, and branch. A screen is not done because it exists; it is done when every pathway on it
behaves as legacy behaves.

| Status | Meaning |
|---|---|
| `GAP` | Confirmed absent on the new side |
| `UNVER` | Something exists; **not** verified pathway-by-pathway against legacy |
| `WALKED` | Legacy screen opened and compared with Todd; observations recorded below |
| `IMPL` | Code written to legacy semantics; **not yet verified** against a legacy run |
| `DONE` | Reproduced and verified against legacy |

`UNVER` is the default and it is not a pass. Nothing becomes `DONE` without a legacy comparison.

Legacy runs at `https://localhost:5003/`, routed `/{jobPath}/{Controller}/{Action}`.
Reference job for this walk: `stateonelacrosse-onsitemerch-2026` (`StoreId 4`, items 13–17).

---

## Inventory status

Mechanically enumerated from legacy source: **StoreItems, StoreSkus, StoreImages, StoreSales,
StoreRefunded, StoreRestocked, StoreCartQuantityAdjustments, StoreAdminAdd, ShoppingCart,
Invoices, StoreFamily/Index (partial), Checkout (partial).**

Since inventoried and ported in full: **StoreDashboard** (three pivots — D-01…D-04, plus its one
dead action) and **the three StoreEmail\* screens** (E-01…E-05).

**NOT yet inventoried at pathway granularity** — these rows are placeholders and will grow:
StoreSalesWalkup, StoreTwoClick, CheckoutConfirmation, WalkUp, and the Labels/Crystal group.

---

## A · Shopper storefront — `StoreFamilyController`

| # | Legacy pathway | Status |
|---|---|---|
| A-01 | `Index` — catalog render, active items only | IMPL |
| A-02 | Catalog order: `SortOrder`, **0 sorts LAST (→10000)**, then `StoreItemName` | IMPL |
| A-03 | Per-item image carousel. **The carousel was already built** in `item-detail`; what was missing was data — `StoreItemImage` held only each item's FIRST instance, so `imageUrls` was never longer than 1. The image sync now indexes every file on disk, so items with 2–3 photos surface all of them | IMPL |
| A-04 | Per-item tabs: Pickup · Return Policy · Contact. BUILT as **one** panel per surface, not one per item — the three strings are JOB-level (`Jobs.StorePickupDetails` / `StoreRefundPolicy` / `StoreContactEmail`), so legacy's tab strip repeated identical text once per product. Storefront gets a collapsible panel, checkout gets the three open lines legacy also had there. See D-10 | BUILT |
| A-05 | `listSoldOutOrInactiveSkus` surfaced per item | IMPL |
| A-33 | ~~Per-item `itemBufferSize` reserve~~ — **CLOSED, not a gap**: legacy declares `private static readonly int itemBufferSize = 0` (`IStoreService.cs:73`). A dead constant that subtracts nothing. | CLOSED |
| A-34 | Availability forced to **0** when the SKU OR its parent item is inactive (`IStoreService.cs:1376`) | IMPL |
| A-35 | **ADN invoice number `{CustomerAi}_{JobAi}_{batchId}_M`** (`IStoreService.CreateAdnInvoiceNumber`). The `_M` suffix is how `adn.MonthyQBPExport_Automated_Merch` finds merch transactions (`charindex('_M', [Invoice Number]) > 0`). Ours built `STORE-{id}`, which never matched — every new-store sale was absent from the monthly remittance export. | IMPL |
| A-06 | Auto-selects when exactly one family player, as legacy | IMPL |
| A-07 | Size/Colour/Quantity. Colour+size auto-select on a single option, as legacy. Quantity now takes the LOWER of legacy's 5-per-add and the shelf — see A-37 | IMPL |
| A-08 | Cart badge with item count | IMPL |
| A-09 | Purchase-history badge with batch count. Hidden outright at zero — the screen behind it would be empty | BUILT |
| A-10 | "No items available for sale at this time" empty state | IMPL |
| A-11 | Add to cart. Ours posts a resolved storeSkuId; legacy resolves (item,colour,size) server-side with `(StoreColorId ?? 0)`. Same outcome, ours unambiguous | IMPL — divergence A-38 |
| A-12 | Cart list with per-line remove. Card layout, not an EJ2 grid — consistent with every other shopper surface | IMPL — presentation divergence |
| A-13 | Recipient, unit price, quantity, line total present. Per-line feeProduct/feeProcessing shown as cart-level Subtotal/Fees instead of per-row columns | IMPL — presentation divergence |
| A-14 | Cart totals present (Subtotal / Fees / Tax / Total). NOTE: 3 of legacy's 4 footer aggregates reference fields absent from its own grid (`storeItemQuantity`, `storeItemTotalPrice`, `paidTotal`) and render empty — a legacy defect, not replicated | IMPL |
| A-15 | `RemoveCartSku` | IMPL |
| A-16 | Empty-cart guard on Checkout navigation | IMPL |
| A-17 | `Checkout` GET — CC form, fee breakdown, total due | IMPL |
| A-18 | Availability re-check → auto-trim + `bCartHasBeenAutoUpdated` banner | BUILT — see D-8 |
| A-19 | Quantity-adjustment audit row on auto-trim | BUILT — see D-8 |
| A-20 | ADN charge, batch settle and StoreCartBatchAccounting row all written | IMPL |
| A-21 | Empty-cart + already-processed guards both present | IMPL |
| A-22 | Confirmation shows Order #, Payment Method, Total Paid, Transaction ID, Invoice #. Payment method was the one missing line; it is the name of the method the shopper actually used, kept from the payment-methods call rather than re-fetched | IMPL |
| A-23 | Confirmation: inline PDF receipt iframe. Legacy inlined the whole PDF as a base64 `data:` URI in the page HTML; ours fetches the same bytes and hands the iframe a blob URL. See D-12 | BUILT |
| A-24 | Walk-up confirmation variant: different copy, no receipt buttons, and the counter session ends once the receipt is on screen. `IsWalkUp` is resolved server-side from the caller regId claim against the job Store Merch anchor. See D-12 | BUILT |
| A-25 | Receipt PDF via GET /store/receipt/{id} + Download Receipt button | IMPL |
| A-26 | Receipt emailed automatically on successful checkout (parents + players). Legacy Priority 3 ordering kept: a mail failure is logged, never surfaced as a failed checkout | BUILT |
| A-27 | `SendEmailReceipt` — resend, from the confirmation and from each Invoices row. Legacy toasted success unconditionally; ours reports what the server actually did, including who it went to | BUILT |
| A-28 | `Invoices` — purchase history. Cards with per-row actions, not an EJ2 grid with a selection toolbar. See D-12 | BUILT |
| A-29 | Download Receipt / Email Receipt, moved from a selection toolbar onto each row | BUILT |
| A-30 | ~~Invoices auto-selects row 0 on databound~~ — **CLOSED, nothing to port**: auto-selection existed only so legacy toolbar buttons were not dead on arrival. With per-row actions there is no selection to prime | CLOSED |
| A-31 | `WalkUpRegister` — mini-registration form + state list. Compared: same eight fields, but legacy validation and the state dropdown were missing. See D-14 | IMPL |
| A-32 | `StoreTwoClick/Login` — family login into store. Compared; deliberate flow divergence, see D-14 | DIVERGE |
| A-36 | Sold-out items stay visible; unbuyable variants are named (`SoldOutOrInactiveSkuLabels`), listing gate is `active && skuCount > 0` as legacy | IMPL |
| A-37 | Quantity cap. **Legacy's rule recovered and kept**: `StoreFamilyController` builds the dropdown from a hard `int maxQuantity = 5` — five of one variant per add, availability never consulted. Ours had the availability clamp but had DROPPED the 5, with a 99 fallback. Now `min(5, availableCount)`, one definition in `store-quantity.ts` shared by both add surfaces, with the fallback at 5 rather than 99 while availability is in flight. The screen names which ceiling was hit ("5 per order" vs "Only N left" vs "Sold out"). The in-cart editor stays uncapped, matching legacy's freely-editable cart grid — 5 limits one add, not what a family may own | BUILT |
| A-38 | Add-to-cart availability basis: legacy checks `GetSkuAvailableCountBySoldAndBuffer` (sold only, NOT in-cart) and relies on the checkout auto-trim; ours deducts in-cart too, refusing earlier. Ours is stricter. **The auto-trim legacy leans on now exists (D-8)**, so this is a deliberate belt-and-braces, not a missing safety net | DIVERGE |
| A-39 | Empty-cart guard on checkout POST (legacy "Fix #1") | IMPL |
| A-40 | Unpaid-lines re-check immediately before charging (legacy "Fix #6") | IMPL |

## B · Catalog configuration

| # | Legacy pathway | Status |
|---|---|---|
| B-01 | `StoreItems/Index` grid — Active · Item · SortOrder | WALKED |
| B-02 | Item grid sorted alphabetically by Item (not SortOrder) | IMPL |
| B-03 | `UpdateItem` writes **SortOrder + Active ONLY** | IMPL |
| B-04 | SortOrder editable in the grid dialog | IMPL |
| B-05 | `CreateNewStoreItem` modal — **4 fields**: name, price (min 1 / max 200 / c2), sizes, colours | IMPL |
| B-06 | Sizes/colours split on `;`, `RemoveEmptyEntries`, then `.Trim()` | IMPL |
| B-07 | SKU matrix size × colour on create, skipping existing combos; SIZE outer / COLOUR inner | IMPL |
| B-08 | Items toolbar: Excel Export. **Inert in legacy** — the toolbar declares the button but the grid never sets `allowExcelExport="true"`, so clicking it does nothing (see D-9). BUILT anyway, server-side: Active · Item · Sort Order | BUILT |
| B-28 | **`StoreColors`/`StoreSizes` are a GLOBAL dictionary** — looked up by name with no store or job filter | IMPL |
| B-29 | Create POST is wired to `hide.bs.modal` — **Cancel and × also submit** | GAP |
| B-30 | `GetOrCreateStoreItemAsync` matches `StoreId + StoreItemName`; on hit reuses the item and does **not** update price/comments | IMPL |
| B-31 | No sizes and no colours → `CreateDefaultSkuAsync`, one null/null SKU | DONE (already correct) |
| B-32 | New SKUs born `Active = true, MaxCanSell = 0`; no MaxCanSell field at creation | IMPL |
| B-33 | `Item Comments` is commented out of the modal; JS sends `null` | DIVERGE (approved) — the DTO already carries `StoreItemComments`, so the field is KEPT on create rather than hidden. Ruling: "if comment is in model/dto already then preserve". |
| B-34 | New items born `SortOrder = 0` → sort **last** on the storefront (see A-02) | IMPL |
| B-35 | Client validation is `if (itemName && itemPrice)` — sizes/colours **not** enforced despite the placeholder | IMPL |
| B-09 | `StoreSkus/Index` grouped by item, collapsible, "N skus" caption | WALKED |
| B-10 | SKU columns: Active · Sku · PickedUp · Sold · UnSold · MaxCanSell · Price | IMPL |
| B-11 | `PickedUp` = `CartBatchSkuItemsSignedFor` | IMPL |
| B-12 | `UnSold = MaxCanSell − Sold` (**no in-cart deduction**) | IMPL |
| B-13 | SKU label `Item:Size:Color`, `::`→`:` collapse when a dimension is null | IMPL |
| B-14 | SKU sort: Item → Size → Colour, alphabetical | IMPL |
| B-15 | `UpdateSku` writes **Active + MaxCanSell ONLY** | IMPL |
| B-16 | `UpdateSku` StoreItemSkuId==0 branch → updates parent item Active | IMPL |
| B-17 | `UpdateSku` batch branch → delete SKUs, then parent item | IMPL |
| B-36 | `UpdateSku` action "remove" branch → delete a single SKU by key | IMPL |
| B-18 | Skus toolbar: Excel Export. **Inert in legacy**, same cause as B-08 (see D-9). BUILT: Item · Active · Sku · PickedUp · Sold · UnSold · MaxCanSell · Price, store-wide — legacy's Skus grid listed every SKU in the job, which our UI folds into the expandable row under each item, so the export restores the flat list | BUILT |
| B-19 | `StoreImages/Index` grid — every image in the job, one row per file. Ours groups by item (`Photos` tab) — same rows, the shape the question is actually asked in | IMPL |
| B-20 | Images toolbar: **Add · Edit · Delete** (only Store screen with create/delete) | IMPL |
| B-21 | Upload, auto-numbered `{storeId}-{storeItemId}-{instance}.jpg`, instance = max+1 | IMPL |
| B-22 | Replace an existing image, keeping its instance and position | IMPL |
| B-23 | Delete an image, with confirm dialog | IMPL |
| B-24 | `MAX_IMAGES_PER_ITEM = 10` cap | IMPL |
| B-25 | Missing-image fallback. Legacy substitutes `missing-image.jpg`; ours renders a CSS placeholder tile, and the admin grid flags the item "Needs a photo". Same outcome, different mechanism | IMPL |
| B-26 | StoreItemId edit is a dropdown of the job's items. **MOOT by shape:** legacy needed it because its grid was flat and a row had to name its item; ours groups by item, so upload is already addressed to a known item and cannot be mis-targeted | CLOSED |
| B-27 | Grid thumbnail. Legacy base64-encodes the local file; ours points at the statics URL. Same outcome, better mechanism | IMPL |
| B-37 | `RenumberImagesAfterDeletion` — instances stay contiguous from 1 after a delete, two-phase so a shift never collides | IMPL |

## C · Sales operations

| # | Legacy pathway | Status |
|---|---|---|
| C-01 | `StoreSales/Index` line-item grid. Ours is the same grain (one row per purchased line) with the columns a director acts on; the 24-column set is trimmed, see R-15 | IMPL |
| C-02 | Columns incl. DirectTo club · agegroup · pool · team · email · cellphone | IMPL |
| C-03 | Excel export **including hidden columns**. BUILT: all 24 data columns in legacy's grid order, following the Walk-ups filter. Two of legacy's 26 columns are deliberately absent and two labels corrected — see D-9 | BUILT |
| C-04 | Excel/Filter menu, paging 10/20/50/100/All, sorting. Ours: free-text filter across item/buyer/team/club; no paging (654 lines across all 1,096 jobs) | IMPL |
| C-05 | Swap command → `GetCartItemSkuOptions` → swap dialog | IMPL |
| C-06 | Swap: target SKU dropdown + quantity, both required | IMPL |
| C-07 | Refund command → `GetCartBatchHasSettledStatus`. Shared with the registration and team refund paths via `IAdnReversalService.GetChargeStatusAsync` | IMPL |
| C-08 | Refund dialog: amount capped at `Paid − Refunded`, restock count capped at qty. **Now enforced SERVER-side too** — legacy capped in the dialog only, so the ceiling was advisory | IMPL |
| C-09 | Unsettled batch → VOID path. Ours states the consequence in the dialog and hides the amount box rather than asking `confirm()` after the fact | IMPL |
| C-10 | Void dialog: batch total paid shown. Legacy also listed the batch's SKUs; ours names the amount and that everything is restocked | IMPL |
| C-11 | Void refunds and restocks **every SKU in the batch** | IMPL |
| C-12 | `UpdateCartSku` — server side of swap/refund/void | IMPL |
| C-13 | `StoreSalesWalkup/Index` — same grid, walk-ups only. Ours is a toggle on the one grid | IMPL |
| C-14 | `StoreRefunded/Index` grid. Compared and completed — see D-13 | IMPL |
| C-15 | `StoreRestocked/Index` grid, `frozenColumns=4`. Compared and completed — see D-13 | IMPL |
| C-16 | `StoreCartQuantityAdjustments/Index` grid | BUILT — Sales tab → Quantity Adjustments |
| C-17 | Adjustments columns incl. parent first/last/email, WhenChanged | BUILT — legacy order, two corrections in D-8 |

## D · Dashboard

| # | Legacy pathway | Status |
|---|---|---|
| D-01 | Sales Rollup pivot — rows item→sku, cols year→month, Units + Sales | BUILT |
| D-02 | Pivot: label filter, value filter, sorting, `C2` format | BUILT |
| D-03 | Product Sales chart (legacy id says Stacked, config says Column) | BUILT |
| D-04 | Sales Rollup chart | BUILT |

## E · Email campaigns

| # | Legacy pathway | Status |
|---|---|---|
| E-01 | `StoreEmailAbandondedCarts` — min/max age-hours dropdowns | BUILT |
| E-02 | Abandoned grid + checkbox column + detail rows | BUILT |
| E-03 | Abandoned: subject + body + SendEmail | BUILT |
| E-04 | `StoreEmailFamiliesThatNeverUsed` — subject + body + SendEmail | BUILT |
| E-05 | `StoreEmailFamiliesThatOrdered` — subject + body + SendEmail | BUILT |

## F · Labels / Crystal

| # | Legacy pathway | Status |
|---|---|---|
| F-01 | Store Bag Labels (pdf) linked from store admin | BUILT — **report file absent server-side**, see F-note |
| F-02 | Store Per Family Pickup Signoff (pdf) linked | BUILT — **report file absent server-side**, see F-note |
| F-03 | Store Per Family Pivot (pdf) linked | BUILT — **report file absent server-side**, see F-note |
| F-04 | `StorePickupSignoff` — commented out of legacy menu, action live | ENDPOINT LIVE, DELIBERATELY UNLINKED — matches legacy's visible surface |

**F-note — the four Store reports do not exist on the Crystal Reports server.**
The endpoints were already correct (`ReportingController.StoreLabels` → `StoreLabels3`, etc.) and
the UI links now exist, so the port is complete on our side. What is missing is upstream:

- The CR deployment at `C:\Websites\TSIC-CR-2025\App_Data\CrystalReports\` holds **110 report
  files. Not one of them starts with "Store".** `StoreLabels3.rpt`, `StorePickupSignoff.rpt`,
  `StorePerPlayerPickup.rpt` and `StorePerPlayerPivot.rpt` are all absent. Every OTHER endpoint
  name we expose has a matching file (`League_Teams.rpt`, `ClubRep_BalanceDue_*.rpt`,
  `camp_excelexport_short.rpt`, …), so the name→file convention is confirmed and the absence is real.
- Only two of the four have a backing proc — `reporting.StoreLabels` and
  `reporting.StorePickupConfirmation`. `StorePerPlayerPickup` and `StorePerPlayerPivot` have
  neither a proc nor a report file.
- **Legacy is in exactly the same position.** Its `appsettings.json` points at the same
  `https://cr2025.teamsportsinfo.com/api/`, so its three Labels menu items hit the same missing
  reports. This is a pre-existing gap in both apps, not a regression introduced by the port.

The buttons are shipped because the pathway is ours to migrate and they start working the moment
the report files are deployed. The UI treats the proxy's `text/plain` error body as a failure
rather than handing the browser a broken "PDF".

## G · Access, config, navigation

| # | Legacy pathway | Status |
|---|---|---|
| G-01 | Job Admin **Merch tab** — 8 fields | WALKED |
| G-02 | `Enable Store` · `Allow Store Walk-up` · Contact Email · Refund Policy · Pickup Details | IMPL |
| G-03 | `Enable STP` on the Merch tab | BUILT — moved to the Teams/ClubReps tab 2026-08-23, see D-6 |
| G-04 | `Store Sales Tax` / `Store TSIC Rate` — no `%` in label | BUILT — see D-6 |
| G-05 | `StoreAdminAdd` — jqGrid roster of Store Admins | BUILT — Store Manager → **Staff** tab |
| G-06 | Store Admin add / edit, username readonly on edit | BUILT — see D-7 |
| G-07 | Store admin menu: 4 groups, 12 live destinations | BUILT except Quantity Adjustments (C-16) |
| G-08 | `Dashboard Home` link, right-aligned | BUILT — the Dashboard tab is the destination |

---

## Decisions taken

**D-1 — Line-item fee semantics. SETTLED, APPLIED.**
Legacy semantics restored across six sites: `RecalculateLineItemFees`, checkout `lineTotal`,
`BuildCartBatchDto.totalFees`, `StoreReceiptService` (grid cell + totals footer),
`StoreCartRepository.LineTotal`, `StoreAnalyticsRepository.LineTotal` ×2. Builds clean.
Also fixed a live display bug — `LineTotal` was rendering a $38.30 line as $75.30.
Read `StoreFamilyController.AddItemToCart` for the authoritative math; do not restate it here.

**D-2 — Store image storage. SETTLED, APPLIED. Disk is truth; the table is an index.**
Legacy defines *what happens*; it does not define *where bytes live*. The resolution keeps both,
each doing the job it is good at:

- **The filesystem is the source of truth**, exactly as in legacy — the files on the statics share
  matching `{storeId}-{storeItemId}-{instance}.jpg` ARE the item's images. Every existing file
  keeps working untouched, and the legacy app and the new one can write the same folder.
- **`stores.StoreItemImage` is a read index**, not a second source of truth. It exists so the
  shopper-facing catalog projects image URLs inside its existing query instead of enumerating a
  directory per item per render — the cost that made legacy's read path expensive.
- **Every mutation re-syncs the index from disk** for the items it touches, so the two cannot
  drift, and reading the admin Photos tab reconciles the whole store.

That last point is not theoretical: the table held 20 rows against 34 files, because it recorded
only each item's FIRST instance. Items with two or three photos showed one in the new catalog and
A-03's carousel had nothing to page through. The sync repairs that on first open — no script, no
schema change. `DisplayOrder` now carries the instance number, which is what orders the carousel.

Written by `StoreImageService` (Infrastructure). B-19…B-27 all ship.

**D-3 — Store email campaigns. SETTLED, APPLIED. Three legacy controllers, one code path.**
`StoreEmailAbandondedCarts`, `StoreEmailFamiliesThatNeverUsed` and `StoreEmailFamiliesThatOrdered`
were byte-for-byte identical below the audience query — same address resolution, same substitution
loop, same `EmailLogs` row, same sender confirmation, all three copied. The port keeps ONE service
(`StoreCampaignService`) whose only branch is the audience; the send rides the existing
`IEmailBatchService` engine that registration-search, My Roster, ARB and USLax already use.

Deliberate divergences, each a defect in legacy rather than a behaviour worth copying:

1. **Unsubscribe is honoured.** Legacy's three store screens tested `BEmailOptOut` nowhere, so a
   family that had clicked unsubscribe still got store blasts. The engine applies opt-out for every
   batch path. A family counts as opted out if ANY of its registrations is — mom and dad share the
   mailbox across every child, so one click silences it.
2. **Inactive cart lines are excluded.** Legacy's abandoned query did not filter `Active`, so a
   voided or removed line was still advertised back to the family as "you left this behind".
3. **`!JOBLINK` → `!STORELINK` in the seeded templates.** Legacy's store templates bound `!JOBLINK`
   to `/{jobPath}/StoreTwoClick/Index` while the same token means the job home page everywhere else.
   The store link gets its own token; `!JOBLINK` keeps its app-wide meaning.
4. **Non-registered families still render tokens.** A store family need not be registered in the job
   — on the reference walk-up store 24 of 27 purchasing families are not, and the substitution
   engine keys entirely off `Registrations`. Without the fallback, those 24 emails would have
   shipped literal `!JOBNAME` text.
5. **`!BATCHCARTSKUS` items are wrapped in `<li>`.** Legacy opened a `<ul>` and appended bare
   strings, so the cart contents rendered as one run-on line.
6. **The headcount equals what sends.** Legacy counted family ids, then dropped address-less
   families mid-send; the screen's number and the result never agreed. Both now resolve through the
   same path, and `skippedNoEmail` reports the difference explicitly.
7. **One availability round-trip, not N.** Legacy called `GetSkuAvailableCountBySoldAndBuffer` per
   line inside a per-cart loop. The stock BASIS is legacy's and load-bearing: `MaxCanSell − Sold`,
   ignoring in-cart quantities. Netting in-cart here would count the very cart being advertised and
   zero out every abandoned cart.
8. **The sender receipt reaches the always-copy list.** Legacy hand-rolled a confirmation to the
   sender alone in each of the three controllers; this uses the shared `BatchCompletionReceipt`.

**D-4 — Store Dashboard. SETTLED, APPLIED. One dataset, three pivots, and a money correction.**
Legacy's `StoreDashboard/Index` rendered three `ejs-pivotview` instances. It fed two from
`GetJobPurchasesPivotData` and the third from a separate inline projection of the same table;
all three now read one endpoint, so the chart and the table above it cannot disagree.

Restoring `GetJobPurchasesPivotData` exposed three divergences in the query we already had, each
overstating a director-facing number. Measured across the live database: **units 533 → 529,
revenue $11,755.21 → $11,662.05.**

1. Units summed `Quantity`, not `Quantity − Restocked` — returned goods counted as sold.
2. Revenue summed `PaidTotal` and ignored `RefundedTotal` entirely, overstating by **$93.16**.
3. The filter was `PaidTotal > 0` rather than "the batch was paid for", so a line refunded down to
   zero vanished from the rollup instead of showing as zero revenue against its units.

`GetSalesByItemAsync` carried the same three and was corrected to match: two readouts of the same
money disagreeing is worse than either being wrong alone.

The per-row zero guard is legacy's and is load-bearing — `PaidTotal == 0 ? 0 : PaidTotal −
RefundedTotal`, applied before the sum, so an unpaid line contributes zero and never a negative.

Two legacy details worth recording so nobody "restores" them:

- **`GetSalesByItemPieData` is dead code.** Its view was commented out, and it computes
  `(CountSold / CountSoldTotal) * 100` on two `int`s — integer division, so every percentage it
  ever produced was 0. Not ported.
- **`storeItemSku` was built by unconditional `':'` concatenation.** In SQL that makes the WHOLE
  label NULL for a sku missing a colour or size, blanking the pivot's row header. No sku in the
  database has a null colour or size today, so this is latent, not live; the port joins the
  non-blank parts instead.

**D-5 — ⛔ OUT OF SCOPE BUT URGENT: `cr2025.teamsportsinfo.com` is serving the Angular app, not
Crystal Reports.** Found while verifying Surface F. This is NOT a store problem — it affects
**every Crystal report in the product**.

Probed from this box:

```
nslookup cr2025.teamsportsinfo.com   -> 204.17.37.202   (PHOENIX, via the wildcard record)
POST https://cr2025.teamsportsinfo.com/api/CrystalReports/Get
  -> HTTP 200, Content-Type: text/html, 11,083 bytes
  -> body is the Angular SPA index.html (VerticalInsure import, --brand-* tokens)
  -> identical ETag and byte count to GET https://cr2025.teamsportsinfo.com/
  -> Last-Modified: Thu, 27 Aug 2026 16:49:53  (yesterday's prod deploy)
```

The same response comes back for a KNOWN-GOOD report (`League_Teams`, whose `.rpt` is present), so
this is not about the store reports — the host has no IIS binding for the Crystal site and every
path falls through to the Angular site.

**Why this is silent:** `ReportingService.ExportCrystalReportAsync` only branches on
`!IsSuccessStatusCode`. A 200 carrying `text/html` is treated as success, and 11KB of HTML is
streamed to the user as `TSIC-Export.pdf`. The user sees a downloaded PDF that will not open.

**SETTLED by Todd, 2026-08-28: "cr2025 website has been turned off."** Not a lost binding, not
DNS — a decision, and consistent with the agreed retirement of Crystal in favour of code-gen off
the Syncfusion file-format libraries. So this is the steady state, not an outage, until that port
lands.

**FIXED (`69736f0e`).** `ExportCrystalReportAsync` now rejects a `text/html` body and returns the
same `TSIC-Export-Error.txt` it already used for a Crystal refusal, so no surface hands the user a
`.pdf` that will not open. The shared test moved to `ReportingService.isErrorPayload` on the
frontend.

**Still open, product-wide:** only the store's Labels buttons call `isErrorPayload` and show a
message. `reports-library`, `report-launcher`, `client-menu`, `produce-job-invoices` and
`x-job-reports-library` still save the error `.txt` — self-describing, but a message on screen
would be better. One shared definition now exists for whoever does that sweep.

**D-6 — Surface G, the config half. Two of the four config gaps were already decided elsewhere;
one was a live money-label defect.**

*G-03 `Enable STP`* — closed by a prior ruling, not by new work. The flag moved to the
Teams/ClubReps tab on 2026-08-23 because enabling Stay-to-Play is the **director's** consent
decision and the Merch tab is `superUserOnly` — no director could ever have reached it there.
Legacy put it on the Merch tab; that placement is the thing we deliberately did not copy.

*G-04 `TSIC Rate`* — a real defect, found by reading the column rather than the label. The screen
said **`TSIC Rate (%)`**, but `Jobs.StoreTSICRate` is a **multiplier**:

```sql
SELECT StoreSalesTax, StoreTSICRate, COUNT(*) FROM Jobs.Jobs GROUP BY StoreSalesTax, StoreTSICRate
--  0.0000   NULL    904
--  0.0000  0.000    186
--  0.0000  0.125      5     <- 12.5%, not 0.125%
--  0.0000  0.100      5     <- 10%
```

A superuser who trusted the label and typed `12.5` would have inflated TSIC's own remittance
figure a hundredfold. It never reaches a buyer (the column feeds
`adn.MonthyQBPExport_Automated_Merch` only), which is exactly why nothing on the storefront ever
exposed the mismatch. Now labelled *(multiplier)*, with the stored value restated as a percent
beside the field. **`Sales Tax (%)` is NOT the same case** and keeps its label — R-13 settled that
one as percent-form, and `SalesTaxMath` is its single conversion point. The two fields sit side by
side and use opposite conventions; the help text on each says which.

---

**D-7 — Store Administrators (G-05/G-06). The roster already existed; what was missing was who
could reach it.**

Every Store Admin registration is already listed, added, edited, activated and deleted by the
SuperUser **Administrators** screen, which manages all seven admin roles at once and enforces the
AM-004 lane wall. That is a superset of legacy's `StoreAdminAdd` — except for reach:

| | read | write |
|---|---|---|
| legacy `StoreAdminAddController` | `StoreAdmin` (Superuser · Director · Store Admin) | `AdminOnly` (Superuser · Director · SuperDirector) |
| new Administrators screen | `SuperUserOnly` | `SuperUserOnly` |

So a **director could not staff their own merch table**. Widening the Administrators screen was
rejected outright: it would let a director mint Directors and SuperDirectors. Instead the new
**Staff** tab carries legacy's policies endpoint for endpoint and is scoped to Store Admin rows
only — the role is a server-side constant, never a request field, and `UpdateAsync` refuses any
registration that is not a Store Admin on the caller's own job. Without that second check a
director holding only a registration id would be able to edit a Superuser.

`IStoreAdminRosterService.AddAsync` **delegates** to `IAdministratorService` rather than
re-implementing eligibility, so the lane wall has one home.

Three deliberate divergences, all recorded on the DTOs:

1. **Adding names an existing account.** Legacy's add branch minted a fresh `AspNetUsers` row
   whose **password was the username**, with gender `"F"` and a 1980-01-01 date of birth, then
   registered it. AM-004 replaced that for every admin role; the typeahead is now the only path in.
2. **Username / first / last are read-only on edit.** Legacy marked them editable in the grid but
   its `Edit` action never read them — only `Active`, `Email` and `Cellphone` were written. Typing
   a new surname there silently did nothing.
3. **No delete.** Legacy defined `deleteOptions` but passed `del: false` to `navGrid`, so delete
   was never on the screen. Clearing Active is how a store admin is retired, then and now.

One improvement over legacy: the email write goes through `UserManager.SetEmailAsync`, which also
rewrites `NormalizedEmail`. Legacy assigned the raw column and left the normalized copy stale —
which is what forgot-password looks accounts up by.

**D-8 — The checkout auto-trim (A-18/A-19), and the grid that reads it (C-16/C-17). Checkout
dead-ended the shopper where legacy quietly fixed the cart.**

Legacy re-checks availability twice — when the shopper ENTERS checkout and again on submit —
trims any line whose stock has gone, logs each change to
`stores.StoreCartBatchSkuQuantityAdjustments`, and redirects with `bCartHasBeenAutoUpdated=true`
so the banner shows before any money moves. None of that existed here. Ours threw:

```
"Items no longer available: SKU IDs 412, 419"
```

— internal ids, no trimmed cart, no way forward except guessing which line to delete.

Now `StoreCartService.TrimBatchToAvailabilityAsync` owns the rule, called from a new
`POST /store/checkout/prepare` (the checkout page's load) and again inside `CheckoutAsync`. The
checkout page names what changed; legacy's banner said only "Your Cart Has Been Updated" and left
the shopper to spot the difference.

**The availability basis is legacy's and it is a deliberate choice, not an oversight.**
`MaxCanSell − Sold`, with legacy's buffer of 0. Units in OTHER people's unpaid carts are **not**
deducted — legacy has a `GetSkuAvailableCountBySoldAndCartAndBuffer` variant and pointedly does
not use it here. Only money takes stock off the shelf; whoever pays first gets it. Reserving
stock for unpaid carts would starve real buyers, and the abandoned-cart campaign exists precisely
because carts sit unpaid for 6 to 48 hours.

`IStoreCartRepository.ValidateBatchAvailabilityAsync` was **removed**, not left beside the new
code. It counted other unpaid carts against `MaxCanSell` and was the source of the throw. A
second availability opinion sitting next to the first is how the next person reintroduces the
bug; the interface now carries a note saying so.

Stock also respects legacy's `StoreItemSkuMaxCanSell`: `(sku.Active AND item.Active) ?
MaxCanSell : 0`, via a new batched `GetEffectiveMaxCanSellAsync`. Deactivating a parent item
empties it from every open cart at checkout, which a raw `MaxCanSell` read would not do.

The audit row is stamped with the **superuser** id, as legacy did — the trim is a system action
taken against the shopper's cart, not something the shopper did. Audit rows, reduced quantities
and removals all go in ONE `SaveChanges`, so there is never a trimmed cart with no record of why.

Two corrections in the admin grid (C-16/C-17), both on the read side:

1. The SKU label went through an unconditional `':'` concat, which in SQL nulls the WHOLE label
   for a SKU with no size or colour. It now uses the shared `StoreSkuLabel`.
2. Legacy's column was named `MomEmail` but read `StoreCart.FamilyUser.Email` — the family
   LOGIN's address, not `Families.Mom_Email`. The name was wrong and the column it read was the
   useful one; the DTO now says `Email` and says why.

Also DRY: four separate implementations of "Item:Size:Color" existed across three repositories,
in three different styles. They are now one `StoreSkuLabel.Build`.

**Note the data.** `stores.StoreCartBatchSkuQuantityAdjustments` holds exactly **one row**
across the whole database (`StoreCartId 109`, 5 → 0, 2026-05-08). The grid will be empty on most
jobs, and that is the correct reading — it is an exception log, not a report. Legacy's sibling
`LogRestock` is worth knowing about while you are here: it builds a `StoreCartBatchSkuRestocks`
row and never `Add`s it before saving, so legacy's restock logging has always been a no-op.

### D-9 · Excel exports — two of legacy's four buttons never worked (B-08, B-18, C-03)

Legacy exported CLIENT-side: the EJ2 grid serialised its own column set in the browser. Two
things follow, and both were verifiable in the markup rather than by running anything.

**The Items and Skus export buttons are dead in legacy.** Both views put `ExcelExport` in
`toolbar` but neither sets `allowExcelExport="true"` on the grid — without it the toolbar item is
inert and the click does nothing. Only `StoreSales`, `StoreRefunded` and
`StoreCartQuantityAdjustments` enable it (17 views project-wide do). So a director who clicked
Export on the Items or Skus screen got silence, for years.

Ruling: **build all four anyway.** The column sets come from legacy's own grid definitions, so
these are the exports legacy intended; replicating two dead buttons would be carbon-copying a
defect, which is the R-10 call again. All four run server-side over the shared
`ExcelWorkbookWriter` — extracted out of `ReportingService`, which is still its largest caller,
so there is one workbook writer in the codebase and not two that drift.

**Every export reads what a screen already reads.** No export-only query exists: items come from
`GetItemsAsync`, sales from `GetSaleLinesAsync`, adjustments from `GetQuantityAdjustmentsAsync`.
The one new repository method, `GetAllSkusWithAvailabilityAsync(storeId)`, shares its projection
with the per-item read through a private `ProjectSkusAsync` — the counts have subtle legacy
semantics (restocks netted, in-cart NOT deducted from UnSold), and a second copy of them is
exactly how a workbook ends up reporting different stock than the tab it was exported from.

**Four corrections to legacy's sales columns**, all from legacy's own markup:

1. `NewSku` and `New Sku Quantity` are the inline editor's scratch fields for the swap command.
   They hold no data on any row, so legacy's `includeHiddenColumn: true` export emitted two
   entirely blank columns. Dropped — 24 columns, not 26.
2. Legacy labels the `Restocked` column **"Refunded"**, the same header as the actual Refunded
   money column. Two identically-named columns that are not the same thing. Ours says
   `Restocked`.
3. Legacy formats `Restocked` as `c2` — it is a UNIT COUNT, so "3 units restocked" exported as
   "$3.00". Ours exports the integer.
4. Booleans go out as Yes/No. Legacy rendered them as checkboxes, which has no cell equivalent,
   and a word is what a reader of a spreadsheet can filter on.

The sales export follows the Walk-ups switch, so the workbook always matches the grid on screen.
All four endpoints read under `StoreAdmin` — the same policy as the grids they export, so a store
admin working the table can pull a pick list without a director present.

R-15 is closed by this: nothing gets promoted on-screen. See its row.

### D-10 · Pickup / Refund Policy / Contact — job-level copy, rendered once (A-04)

Three strings the director writes on the job config Store tab: `Jobs.StorePickupDetails`,
`Jobs.StoreRefundPolicy`, `Jobs.StoreContactEmail`. Legacy put them in two shopper surfaces —
a Pickup · Return Policy · Contact tab strip inside EVERY item card on `StoreFamily/Index`, and
three labelled lines on `StoreFamily/Checkout`.

**Rendered once per surface, not once per item.** The strings are job-level, so legacy's tab
strip showed twelve identical copies of the pickup instructions in a twelve-product store. Same
copy, same two places a shopper meets it, without the duplication: a collapsible panel above the
storefront list, and always-open lines at checkout (the shopper is one button from paying and
should not have to click to find the refund policy).

**One name for one field.** Legacy labelled it "Return Policy" on the tab and "Refund Policy" at
checkout. The column is `StoreRefundPolicy`; it says Refund Policy everywhere here.

**Plain text, interpolated — never `[innerHTML]`.** Job config collects these in bare
`textarea`s and legacy rendered them through Razor's HTML-encoding interpolation, so they have
never been HTML. `white-space: pre-line` keeps the line breaks the director typed.

Two details that matter with 1,096 jobs, most of which leave all three blank:
`GetStoreFrontInfoAsync` trims blank-but-present to null at the repository (blank and absent are
the same thing to a shopper) and returns `HasAny`, so the surfaces render *nothing* rather than
an empty panel with three blank headings. A failed fetch is silent for the same reason — this is
supporting copy, and a shopper who cannot see the pickup note must still be able to buy.

The read is a new `GET /store/storefront-info` under plain `[Authorize]`, matching
`GET /store/items` — this is what a SHOPPER reads, so it must not sit behind `StoreAdmin` the way
the admin store-identity endpoint does. It is deliberately NOT folded into `JobStoreConfig`:
that record is the money/ADN config and has no business growing a tail of display text.

### D-11 · Scope-boundary bug class — seven endpoints where the id WAS the credential

Found while wiring the receipt email (A-25). `GET /store/receipt/{storeCartBatchId}` took the
batch id, looked it up with no predicate but that id, and rendered the PDF under the *reader's*
job name — so any authenticated family could pull any other family's receipt, in any of the
1,096 jobs, and it would look like their own store's document. Grepping for the shape found six
more of it.

**The shape.** The endpoint accepts an entity id from the URL *and* a `jobId` (or the caller's
`familyUserId`) — and the repository query keys on the id alone. The extra parameter is threaded
through the controller and the service and then never reaches a `WHERE`. The id is the only
credential, and ids are sequential integers.

| # | Endpoint | What a caller could reach |
|---|---|---|
| 1 | `GET /store/receipt/{id}` | any family's receipt PDF, any job |
| 2 | `DELETE /store/cart/items/{id}` | delete any family's cart line — response returned the victim's cart |
| 3 | `PUT /store/cart/items/{id}/quantity` | rewrite any family's cart line |
| 4 | `POST /store/cart/items` | add another job's SKU, at that job's price |
| 5 | `PUT /store/skus/{id}` | store admin flips Active/MaxCanSell on another job's SKU |
| 6 | `GET /store/items/{id}/skus` | store admin reads another job's stock and prices |
| 7 | `POST /store/restock` | store admin restocks another job's line — inventory write *and* audit row |
| — | `GET /store/skus/{id}/availability` (+ batch) | cross-job stock disclosure |

Not a legacy defect to preserve: legacy is a per-job MVC app where the session carries the job,
and its queries inherit the boundary from `Session["JobId"]`. This is a shape our stateless API
introduced.

**The remedy is structural, not per-call-site.** Guards at each call site are one forgotten
`if` from regressing, and the next endpoint written against the same repository method starts
unguarded. The unscoped readers are DELETED so a scope-free overload cannot be called:

- `GetSkuByIdAsync(int)` → `GetSkuInStoreAsync(int storeSkuId, int storeId)`. The `StoreId`
  predicate *is* the authorization check.
- `GetLineItemByIdAsync(int)` → **two** methods, not one nullable "family, or null for staff"
  parameter: `GetLineItemForFamilyAsync(int, int storeId, string familyUserId)` for shopper
  actions, `GetLineItemInStoreAsync(int, int storeId)` for staff actions. Which boundary applies
  is then a compile-time choice at the call site instead of an argument someone can pass `null`.

Same reasoning that deleted `ValidateBatchAvailabilityAsync` (R-15): when the safe and unsafe
paths are two overloads of one name, the unsafe one gets called.

The receipt is the one case an id-only lookup is *nearly* right — a family reads their own — so
it gets both checks explicitly in the controller: `CallerMayReadReceipt` compares
`StoreReceiptContextDto.FamilyUserId` to the caller for shoppers, and staff are allowed through
by role, while `GenerateReceiptPdfAsync` independently refuses when `context.JobId != jobId`.
Two boundaries because there are two ways to be wrong here: wrong family, wrong job.

**Explicitly NOT built:** a generic ownership filter or middleware. Ownership differs per
entity — a SKU belongs to a store, a cart line belongs to a store *and* a family, a batch belongs
to a job — and a filter that has to be told which is which is the same `if` in a costume.

Verified clean and left alone, which is where the pattern above came from:
`GetSwapOptionsAsync`/`GetBatchSettledStatusAsync` (`LoadLineInStoreAsync`,
`AssertBatchInStoreAsync`), `GetItemDetailAsync`, `DeleteSkuAsync`/`DeleteItemAsync`
(`AssertItemBelongsToJobAsync`).

### D-12 · Receipts and Invoices — the shopper's copy of what they bought (A-23…A-30)

Four legacy pathways with one subject: the receipt. It is emailed on checkout, shown on the
confirmation, downloadable, resendable, and reachable later from a purchase-history screen.

**The receipt is fetched, not inlined.** Legacy base64-encoded the whole PDF into the
confirmation page's HTML (`ViewBag.InvoicePdfBase64` → a `data:` URI iframe). Ours requests the
same bytes from `GET /store/receipt/{id}` and hands the iframe a blob URL, revoked on destroy.
Same document on screen; a several-hundred-KB attachment stays out of the DOM and off the
server-rendered page.

**Walk-up ends the session, once the receipt is up.** Legacy's `CheckoutConfirmation` calls
`SignoutCustomAsync` while rendering when the caller's team is "Store Merch" — the counter
tablet is shared, and the next customer must not inherit this one's account. Order matters here:
the PDF request carries the token, so the sign-out waits until the bytes are in hand. Clearing
auth does not disturb the page (route guards run on navigation), so the customer keeps reading
their receipt and their next click lands on the store login — which is what legacy did too.

`IsWalkUp` is resolved server-side, in the controller, from the immutable regId claim: legacy
compared the caller's team NAME to the literal "Store Merch", ours compares team ids against
`ITeamRepository.GetStoreMerchTeamIdAsync`, the anchor resolver the rest of this subsystem
already uses. Same rule, one place the string is spelled. Fails closed.

**Invoices is cards with per-row actions, not a grid with a selection toolbar.** Legacy's EJ2
grid put Download Receipt and Email Receipt on the toolbar, acting on the selected row, and
auto-selected row 0 on databound (A-30) so the toolbar was never dead on arrival. The buttons sit
on each row here. Three things follow: the select-then-act step disappears, legacy's "Please
select a row with an invoice number first" alert becomes a state that cannot occur, and A-30 has
nothing left to port. It also matches the card layout every other shopper surface in this port
uses (A-12).

Two legacy grid details deliberately not carried: `paymentMethod` is declared `format="C2"` — a
currency format on a string column — and the selection guard tests `batchId`, the primary key,
while its message talks about the invoice number. Neither survives the move to per-row buttons.

**The resend toast reports what happened.** Legacy's `success:` handler fired
"E-Mail sent SUCCESSFULLY" for any 200, including the common case of a family with no address on
file, where nothing was sent. `StoreReceiptEmailResult` carries `sent`, `recipients` and
`reason`; the screen names the addresses on success and the reason otherwise.

**Purchase history is one read, two consumers.** `GET /store/purchase-history` is scoped to the
caller's own family inside the query — there is no id in the URL to tamper with — and backs both
the Invoices list and the storefront badge (A-09). The badge is hidden at zero rather than shown
as "0", since the screen behind it would be empty.

### D-13 · Refunded and Restocked column sets, compared (C-14, C-15)

Both screens had been carried across at about half of legacy's width. Compared column by column
against the live grids and completed.

**Read the Razor, not the column list.** Legacy's `StoreRefunded/Index` appears to declare 26
columns; 14 of them sit inside a `@* … *@` block. The live grid is the first 12 — Item · Color ·
Size · Active · Quantity · $Product · $Processing · $FeeTotal · $Paid · $Refunded · $Refundable ·
Restocked. Everything the commented block held is refund-EVENT detail (RefundDate, RefundType,
TxRefund, Comment, RefundedBy), so what a director actually sees is the purchased LINE and its
refund state, not the refund transaction. That is what is built.

Six columns were missing here: Active, $Product, $Processing, $FeeTotal, $Refundable, Restocked.
`$Refundable` is the one a director acts on. Customer and Date are kept — legacy has them only in
the commented block, but they were already built and dropping them would take away the two things
that identify a row.

**`SkuRefundable` is `FeeTotal − RefundedTotal`, and that is legacy's formula.** Legacy's own
refund dialog caps at `Paid − Refunded`, so the two disagree on paper. Measured: 654 lines, 178
where `FeeTotal ≠ PaidTotal`, and in every one of them the line is unpaid — `PaidTotal` is 0 while
`FeeTotal` carries what is owed. A line that was never paid cannot be refunded, so the difference
is unreachable on this grid and legacy's formula is kept verbatim.

Ordering is legacy's: Item → Colour → Size, not refund date. This is read as an inventory list,
and the variants of one product belong next to each other.

**Restocked** was missing nine of legacy's twelve: BatchId, CartSkuId, quantity bought, Paid,
Refunded, purchase date, Family, Player. All added; `ModifiedBy` is ours and stays — legacy tracks
who refunded (in its commented block) but never who restocked.

One legacy defect not replicated, the same shape as D-9's: `RestockDate` is labelled "Purchased",
duplicating the header of the column beside it. Ours says Restock Date.

Both tables are near-empty on live data — 5 refunded lines, 0 restocks across all 1,096 jobs — so
this is about the screens being right when they are first used, not about fixing what a director
is looking at today.

### D-14 · Walk-up form and the store login flow, compared (A-31, A-32)

**A-31 — same eight fields, none of legacy's rules.** `FirstName · LastName · Email · Phone ·
Address · City · State · Zip` match one for one. What was missing was everything legacy's
`StoreWalkUpRegistrationDto` annotations enforced:

| Rule | Legacy | Was |
|---|---|---|
| Email is an address | `[EmailAddress]` | non-empty |
| Phone is exactly 10 digits | `^([0-9]{10})$` | non-empty |
| ZIP is `12345` or `12345-6789` | `^\d{5}(-\d{4})?$` | non-empty |
| State comes from a list | `<select asp-items="ViewBag.listStates">` | free-text, `maxlength=2` |

All four restored. The state box is now the same options every other address form in this app
uses, via `FormFieldDataService` — a two-character free-text field is how "CA", "Cal" and
"california" end up in one column.

The three format rules are enforced on **both** sides, and the server side is the one that
matters: `POST /store/walk-up-register` is `[AllowAnonymous]`, and one accepted POST mints a
user, a family and a registration. A junk phone or ZIP there is a permanent row, not a rejected
form. `[ApiController]` turns the annotations into a 400 with no controller code.

**A-32 — accepted divergence, not a gap.** Legacy's `StoreTwoClick` is what its name says: check
the password, then silently pick the family's FIRST registration in this job matching
`BActive && AssignedTeamId != null && RoleId == Player`, sign in as Family, land in the store.
No players matched → "Family account {x} does not have any players registered for this event"
and back to the event home.

Ours goes through the app's standard two-phase auth: sign in, choose the registration, land in
the store — one more click, and the empty case is handled by role-selection rather than by a
store-specific message. Rebuilding an auto-select path would mean a second entry into the
authentication system for one screen's convenience; the store is not the place to fork auth.

One legacy restriction deliberately not carried: legacy required `AssignedTeamId != null`, so a
family whose player was registered but not yet rostered could not reach the store at all. That
is a restriction, not a capability, and it blocks a customer who wants to spend money. The DirectTo
dropdown on each product already lets a shopper buy for any of their players, so which
registration they signed in under barely reaches the purchase.

## Open recommendations

| ID | Recommendation | Status |
|---|---|---|
| R-01 | ~~Sales tax multiplier vs percent~~ — **MOOT. There is no sales tax in the fee model.** All 654 `StoreCartBatchSkus` rows carry `SalesTax = 0`, and the remittance export has no tax line. The two tracked figures are the CC processing fee and TSIC's percent of sales. | CLOSED |
| R-02 | ~~Remove the Sales Tax field from config~~ — **WITHDRAWN.** Tax is a future obligation, not dead weight; the field stays, correctly bounded and labelled. Superseded by R-13/R-14. | CLOSED |
| R-03 | ~~Decide whether `Enable STP` belongs on the new store tab~~ — **DECIDED 2026-08-23: no.** It moved to the Teams/ClubReps tab. STP is a director's consent decision, and the Merch tab is superUserOnly, so no director could ever have reached it there. G-03 closed. | CLOSED |
| R-04 | Add a Sort Order control to the items editor | OPEN |
| R-05 | Post-creation price editing — **RESOLVED: locked, per legacy.** Name/price/comments are read-only on edit; the modal now edits Active + SortOrder, which is all `UpdateItem` writes. | CLOSED |
| R-06 | Sort the items list by SortOrder, or offer both | OPEN |
| R-07 | Add `PickedUp` to the SKU panel | OPEN |
| R-08 | Keep legacy `UnSold` as its own column alongside In Cart | OPEN |
| R-09 | Preserve legacy's alphabetical size ordering | OPEN |
| R-12 | ~~Config screen labels **both** `Sales Tax (%)` and `TSIC Rate (%)`, but the two columns use OPPOSITE conventions.~~ **DONE.** `TSIC Rate` now reads *(multiplier)*, carries "0.125 is 12.5%" help text, and restates the stored value as a percent beside it; `step` is 0.001. Sales Tax keeps `(%)` per R-13 and gains its own help line. Verified against the live DB: every job has `StoreSalesTax = 0.0000`, and the only non-zero `StoreTSICRate` values are **0.125 (5 jobs)** and **0.100 (5 jobs)** — decimals, not percents. G-04 closed. | IMPL |
| R-13 | Sales tax conventions settled in code: `SalesTaxMath.ToTaxMultiplier` (percent-form, clamped 0-12) is the single conversion point, and `SalesTaxMath.TaxableBase` names what tax applies to. Deliberate documented divergence — legacy's multiplier arithmetic is unreachable code (654/654 rows at zero) and would charge 100x. | IMPL |
| R-10 | Do **NOT** replicate B-29 (`hide.bs.modal` fires the POST, so Cancel and × also create the item). It is a legacy defect, not a feature; our modal submits only from the Create button. | OPEN |
| R-11 | ~~itemBufferSize~~ — **WITHDRAWN**, see A-33. | CLOSED |
| R-15 | ~~Legacy's sales grid carries 24 columns, most hidden behind the column chooser and only reachable via Excel export.~~ **SETTLED with C-03.** The screen keeps the 9 columns a director acts on; the workbook carries all 24. Nothing is promoted on-screen: the hidden set is the buyer's club/agegroup/pool/team and their email and cellphone, which are reference data for a mail-merge or a pick list, not a decision a director makes at the grid. They are one click away and they are complete, which is what they were for. | CLOSED |
| R-16 | **Walk-up identification was wrong on the new side and is now fixed.** `IsWalkUp` tested "no line has a DirectToRegId"; walk-ups DO have one, pointing at the Store Merch counter registration `StoreWalkUpService` mints. On the dev DB the old rule found **2** batches where legacy's finds **36**, and only 3 of 654 lines have a null `DirectToRegId` at all. Now one definition (`StoreAnalyticsRepository.WalkUpLines`) serves both the payments grid and the sales grid. | IMPL |
| R-17 | **Swap fee split diverges from legacy, deliberately.** Legacy recomputed the split-off line's processing fee and tax from TODAY's job rates and subtracted those from the original, so a rate change since purchase left the two halves not summing to what the customer paid. An exchange moves no money, so ours apportions every money column by quantity and leaves the rounding remainder on the original line — the halves always sum exactly. Note `StoreCartBatchSkuEdits` is **0 rows**: the legacy swap has never run in production, so there is no historical behaviour to match. | IMPL |
| R-18 | **A line-level refund on an UNSETTLED charge reverses the whole purchase.** Authorize.Net has no partial void, so the gateway reverses everything whatever was asked for. Legacy booked that as a line refund, leaving the batch's other lines marked paid with no money behind them. Ours treats the gateway's answer as authoritative, books the full batch, and says so in the message. The dialog also asks the settled status up front so the case is usually avoided rather than explained after. | IMPL |

## Evidence worth keeping

- `StoreSalesTax` is `0.0000` on **all 1,100 jobs**; `StoreTsicrate` on the 7 store-enabled jobs
  is NULL / 0.000 / 0.100 / 0.125 — multipliers. This is the whole basis for R-01.
- 476 paid `StoreCartBatchSkus` rows exist (2025-09-13 → 2026-06-30). **Zero** were written by
  the new store. The first new-app sale is the first row in the new shape.
- Legacy on the dev box shows `missing-image.jpg` for `StoreId 4` items 13–17 because its local
  `wwwroot\images\Store-Sku-Images\` lacks those files. All five resolve `200` on statics.
  Dev-box gap, not a defect on either side.

## Walk log

| Step | Screen | Date |
|---|---|---|
| 0.1 | `/Job/Admin` → Merch tab | 2026-08-27 |
| 1.2 | `/StoreItems/Index` | 2026-08-27 |
| 1.3 / 1.4 | `/StoreSkus/Index` | 2026-08-27 |
| 1.5 | `/StoreImages/Index` | 2026-08-27 |
| 1.1 | `/StoreItems/Index` → Create New Store Item modal | 2026-08-27 |

**Phase 1 walk COMPLETE.** Not yet walked: all of Phases 2–6.

### Correction log

- Reported the create modal as 5 fields at step 1.2; it is **4**. `Item Comments` is inside an
  `@* … *@` block and the JS hardcodes `itemComments = null`. Misread a commented-out block as live.
- Graded pickup-signing as "new, no legacy equivalent" in the retired ledger; legacy has
  `CartBatchSkuItemsSignedFor` and surfaces it as the `PickedUp` column (B-11).
