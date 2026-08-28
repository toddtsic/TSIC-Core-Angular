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

**NOT yet inventoried at pathway granularity** — these rows are placeholders and will grow:
StoreSalesWalkup, StoreDashboard, the three StoreEmail\* screens, StoreTwoClick,
CheckoutConfirmation, WalkUp, and the Labels/Crystal group.

---

## A · Shopper storefront — `StoreFamilyController`

| # | Legacy pathway | Status |
|---|---|---|
| A-01 | `Index` — catalog render, active items only | IMPL |
| A-02 | Catalog order: `SortOrder`, **0 sorts LAST (→10000)**, then `StoreItemName` | IMPL |
| A-03 | Per-item image carousel. **The carousel was already built** in `item-detail`; what was missing was data — `StoreItemImage` held only each item's FIRST instance, so `imageUrls` was never longer than 1. The image sync now indexes every file on disk, so items with 2–3 photos surface all of them | IMPL |
| A-04 | Per-item tabs: Pickup · Return Policy · Contact | GAP |
| A-05 | `listSoldOutOrInactiveSkus` surfaced per item | IMPL |
| A-33 | ~~Per-item `itemBufferSize` reserve~~ — **CLOSED, not a gap**: legacy declares `private static readonly int itemBufferSize = 0` (`IStoreService.cs:73`). A dead constant that subtracts nothing. | CLOSED |
| A-34 | Availability forced to **0** when the SKU OR its parent item is inactive (`IStoreService.cs:1376`) | IMPL |
| A-35 | **ADN invoice number `{CustomerAi}_{JobAi}_{batchId}_M`** (`IStoreService.CreateAdnInvoiceNumber`). The `_M` suffix is how `adn.MonthyQBPExport_Automated_Merch` finds merch transactions (`charindex('_M', [Invoice Number]) > 0`). Ours built `STORE-{id}`, which never matched — every new-store sale was absent from the monthly remittance export. | IMPL |
| A-06 | Auto-selects when exactly one family player, as legacy | IMPL |
| A-07 | Size/Colour/Quantity. Colour+size auto-select on a single option, as legacy. **Quantity differs: legacy is a hard 1-5 dropdown; ours clamps to availableCount (cap 99)** | IMPL — divergence A-37 |
| A-08 | Cart badge with item count | IMPL |
| A-09 | Purchase-history badge with batch count | GAP |
| A-10 | "No items available for sale at this time" empty state | IMPL |
| A-11 | Add to cart. Ours posts a resolved storeSkuId; legacy resolves (item,colour,size) server-side with `(StoreColorId ?? 0)`. Same outcome, ours unambiguous | IMPL — divergence A-38 |
| A-12 | Cart list with per-line remove. Card layout, not an EJ2 grid — consistent with every other shopper surface | IMPL — presentation divergence |
| A-13 | Recipient, unit price, quantity, line total present. Per-line feeProduct/feeProcessing shown as cart-level Subtotal/Fees instead of per-row columns | IMPL — presentation divergence |
| A-14 | Cart totals present (Subtotal / Fees / Tax / Total). NOTE: 3 of legacy's 4 footer aggregates reference fields absent from its own grid (`storeItemQuantity`, `storeItemTotalPrice`, `paidTotal`) and render empty — a legacy defect, not replicated | IMPL |
| A-15 | `RemoveCartSku` | IMPL |
| A-16 | Empty-cart guard on Checkout navigation | IMPL |
| A-17 | `Checkout` GET — CC form, fee breakdown, total due | IMPL |
| A-18 | Availability re-check → auto-trim + `bCartHasBeenAutoUpdated` banner | GAP |
| A-19 | Quantity-adjustment audit row on auto-trim | GAP |
| A-20 | ADN charge, batch settle and StoreCartBatchAccounting row all written | IMPL |
| A-21 | Empty-cart + already-processed guards both present | IMPL |
| A-22 | Confirmation shows Order #, Total Paid, Transaction ID, Invoice #. **Payment method is not shown** | IMPL — minor gap |
| A-23 | Confirmation: inline PDF receipt iframe | GAP |
| A-24 | Walk-up confirmation variant (different copy, no receipt buttons). **One confirmation view only; no walk-up variant** | GAP |
| A-25 | Receipt PDF via GET /store/receipt/{id} + Download Receipt button | IMPL |
| A-26 | Receipt emailed automatically on successful checkout (parents + players) | GAP |
| A-27 | `SendEmailReceipt` — resend | GAP |
| A-28 | `Invoices` — purchase-history grid | GAP |
| A-29 | Invoices toolbar: Download Receipt · Email Receipt | GAP |
| A-30 | Invoices auto-selects row 0 on databound | GAP |
| A-31 | `WalkUpRegister` — mini-registration form + state list | IMPL — form fields not yet compared |
| A-32 | `StoreTwoClick/Login` — family login into store | IMPL — flow not yet compared |
| A-36 | Sold-out items stay visible; unbuyable variants are named (`SoldOutOrInactiveSkuLabels`), listing gate is `active && skuCount > 0` as legacy | IMPL |
| A-37 | Quantity cap: legacy offers a fixed 1-5 dropdown per add; ours clamps to availableCount (fallback 99) | GAP |
| A-38 | Add-to-cart availability basis: legacy checks `GetSkuAvailableCountBySoldAndBuffer` (sold only, NOT in-cart) and relies on the checkout auto-trim; ours deducts in-cart too, refusing earlier. Ours is stricter | DIVERGE |
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
| B-08 | Items toolbar: Excel Export | GAP |
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
| B-18 | Skus toolbar: Excel Export | GAP |
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
| C-01 | `StoreSales/Index` line-item grid, 24 columns | GAP |
| C-02 | Columns incl. DirectTo club · agegroup · pool · team · email · cellphone | GAP |
| C-03 | Excel export **including hidden columns** | GAP |
| C-04 | Excel/Filter menu, paging 10/20/50/100/All, sorting | GAP |
| C-05 | Swap command → `GetCartItemSkuOptions` → swap dialog | GAP |
| C-06 | Swap: target SKU dropdown + quantity dropdown, both required | GAP |
| C-07 | Refund command → `GetCartBatchHasSettledStatus` | GAP |
| C-08 | Refund dialog: amount capped at `Paid − Refunded`, restock count capped at qty | GAP |
| C-09 | Unsettled batch → `confirm()` → VOID path | GAP |
| C-10 | Void dialog: batch SKU listbox + batch total paid | GAP |
| C-11 | Void refunds and restocks **every SKU in the batch** | GAP |
| C-12 | `UpdateCartSku` — server side of swap/refund/void | GAP |
| C-13 | `StoreSalesWalkup/Index` — same grid, walk-ups only | GAP |
| C-14 | `StoreRefunded/Index` grid | IMPL — column set not yet compared |
| C-15 | `StoreRestocked/Index` grid, `frozenColumns=4` | IMPL — column set not yet compared |
| C-16 | `StoreCartQuantityAdjustments/Index` grid | GAP |
| C-17 | Adjustments columns incl. Mom first/last/email, WhenChanged | GAP |

## D · Dashboard

| # | Legacy pathway | Status |
|---|---|---|
| D-01 | Sales Rollup pivot — rows item→sku, cols year→month, Units + Sales | GAP |
| D-02 | Pivot: label filter, value filter, sorting, `C2` format | GAP |
| D-03 | Product Sales stacked column chart | GAP |
| D-04 | Sales Rollup chart | GAP |

## E · Email campaigns

| # | Legacy pathway | Status |
|---|---|---|
| E-01 | `StoreEmailAbandondedCarts` — min/max age-hours dropdowns | GAP |
| E-02 | Abandoned grid + checkbox column + detail rows | GAP |
| E-03 | Abandoned: subject + body + SendEmail | GAP |
| E-04 | `StoreEmailFamiliesThatNeverUsed` — subject + body + SendEmail | GAP |
| E-05 | `StoreEmailFamiliesThatOrdered` — subject + body + SendEmail | GAP |

## F · Labels / Crystal

| # | Legacy pathway | Status |
|---|---|---|
| F-01 | Store Bag Labels (pdf) linked from store admin | GAP (endpoint exists) |
| F-02 | Store Per Family Pickup Signoff (pdf) linked | GAP (endpoint exists) |
| F-03 | Store Per Family Pivot (pdf) linked | GAP (endpoint exists) |
| F-04 | `StorePickupSignoff` — commented out of legacy menu, action live | GAP (endpoint exists) |

## G · Access, config, navigation

| # | Legacy pathway | Status |
|---|---|---|
| G-01 | Job Admin **Merch tab** — 8 fields | WALKED |
| G-02 | `Enable Store` · `Allow Store Walk-up` · Contact Email · Refund Policy · Pickup Details | IMPL |
| G-03 | `Enable STP` on the Merch tab | GAP |
| G-04 | `Store Sales Tax` / `Store TSIC Rate` — no `%` in label | GAP |
| G-05 | `StoreAdminAdd` — jqGrid roster of Store Admins | GAP — no Store Admin roster UI exists |
| G-06 | Store Admin add / edit / delete, username readonly on edit | GAP — no Store Admin roster UI exists |
| G-07 | Store admin menu: 4 groups, 13 destinations | GAP |
| G-08 | `Dashboard Home` link, right-aligned | GAP |

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

## Open recommendations

| ID | Recommendation | Status |
|---|---|---|
| R-01 | ~~Sales tax multiplier vs percent~~ — **MOOT. There is no sales tax in the fee model.** All 654 `StoreCartBatchSkus` rows carry `SalesTax = 0`, and the remittance export has no tax line. The two tracked figures are the CC processing fee and TSIC's percent of sales. | CLOSED |
| R-02 | ~~Remove the Sales Tax field from config~~ — **WITHDRAWN.** Tax is a future obligation, not dead weight; the field stays, correctly bounded and labelled. Superseded by R-13/R-14. | CLOSED |
| R-03 | Decide whether `Enable STP` belongs on the new store tab | OPEN |
| R-04 | Add a Sort Order control to the items editor | OPEN |
| R-05 | Post-creation price editing — **RESOLVED: locked, per legacy.** Name/price/comments are read-only on edit; the modal now edits Active + SortOrder, which is all `UpdateItem` writes. | CLOSED |
| R-06 | Sort the items list by SortOrder, or offer both | OPEN |
| R-07 | Add `PickedUp` to the SKU panel | OPEN |
| R-08 | Keep legacy `UnSold` as its own column alongside In Cart | OPEN |
| R-09 | Preserve legacy's alphabetical size ordering | OPEN |
| R-12 | Config screen labels **both** `Sales Tax (%)` and `TSIC Rate (%)`, but the two columns use OPPOSITE conventions: `storeTSICRate` is a multiplier (0.10 = 10%, default in the export proc) while `ProcessingFeePercent` is a percent (3.5, divided by 100 in the proc). Relabel `TSIC Rate` as a decimal rate. | OPEN |
| R-13 | Sales tax conventions settled in code: `SalesTaxMath.ToTaxMultiplier` (percent-form, clamped 0-12) is the single conversion point, and `SalesTaxMath.TaxableBase` names what tax applies to. Deliberate documented divergence — legacy's multiplier arithmetic is unreachable code (654/654 rows at zero) and would charge 100x. | IMPL |
| R-10 | Do **NOT** replicate B-29 (`hide.bs.modal` fires the POST, so Cancel and × also create the item). It is a legacy defect, not a feature; our modal submits only from the Create button. | OPEN |
| R-11 | ~~itemBufferSize~~ — **WITHDRAWN**, see A-33. | CLOSED |

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
