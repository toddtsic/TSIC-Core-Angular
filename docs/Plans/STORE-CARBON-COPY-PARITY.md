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
| A-01 | `Index` — catalog render, active items only | UNVER |
| A-02 | Catalog order: `SortOrder`, **0 sorts LAST (→10000)**, then `StoreItemName` | GAP |
| A-03 | Per-item image carousel, multiple images, Fade, prev/next templates | UNVER |
| A-04 | Per-item tabs: Pickup · Return Policy · Contact | GAP |
| A-05 | `listSoldOutOrInactiveSkus` surfaced per item | UNVER |
| A-06 | DirectTo recipient select (family players) | UNVER |
| A-07 | Size select, Colour select, Quantity | UNVER |
| A-08 | Cart badge with item count | UNVER |
| A-09 | Purchase-history badge with batch count | GAP |
| A-10 | "No items available for sale at this time" empty state | UNVER |
| A-11 | `AddItemToCartRequest` → `AddItemToCart` | UNVER |
| A-12 | `ShoppingCart` — grid, frozen columns, per-line delete command | UNVER |
| A-13 | Cart column set incl. `directTo`, `feeProduct`, `feeProcessing`, `owesTotal` | UNVER |
| A-14 | Cart footer aggregates (4 Sum columns) | UNVER |
| A-15 | `RemoveCartSku` | UNVER |
| A-16 | Empty-cart guard on Checkout navigation | UNVER |
| A-17 | `Checkout` GET — CC form, fee breakdown, total due | UNVER |
| A-18 | Availability re-check → auto-trim + `bCartHasBeenAutoUpdated` banner | GAP |
| A-19 | Quantity-adjustment audit row on auto-trim | GAP |
| A-20 | `Checkout` POST — ADN charge, batch settle, accounting rows | UNVER |
| A-21 | Empty-cart guard on POST (duplicate-submit protection) | UNVER |
| A-22 | `CheckoutConfirmation` — success panel, invoice #, method, amount | UNVER |
| A-23 | Confirmation: inline PDF receipt iframe | GAP |
| A-24 | Confirmation: walk-up variant (different copy, no receipt buttons) | UNVER |
| A-25 | `GenerateInvoice` — receipt PDF download | UNVER |
| A-26 | Receipt emailed automatically on successful checkout (parents + players) | GAP |
| A-27 | `SendEmailReceipt` — resend | GAP |
| A-28 | `Invoices` — purchase-history grid | GAP |
| A-29 | Invoices toolbar: Download Receipt · Email Receipt | GAP |
| A-30 | Invoices auto-selects row 0 on databound | GAP |
| A-31 | `WalkUpRegister` — mini-registration form + state list | UNVER |
| A-32 | `StoreTwoClick/Login` — family login into store | UNVER |

## B · Catalog configuration

| # | Legacy pathway | Status |
|---|---|---|
| B-01 | `StoreItems/Index` grid — Active · Item · SortOrder | WALKED |
| B-02 | Item grid sorted alphabetically by Item (not SortOrder) | GAP |
| B-03 | `UpdateItem` writes **SortOrder + Active ONLY** | IMPL |
| B-04 | SortOrder editable in the grid dialog | GAP |
| B-05 | `CreateNewStoreItem` modal — **4 fields**: name, price (min 1 / max 200 / c2), sizes, colours | WALKED |
| B-06 | Sizes/colours split on `;`, `RemoveEmptyEntries`, then `.Trim()` | WALKED |
| B-07 | SKU matrix size × colour on create, skipping existing combos; SIZE outer / COLOUR inner | IMPL |
| B-08 | Items toolbar: Excel Export | GAP |
| B-28 | **`StoreColors`/`StoreSizes` are a GLOBAL dictionary** — looked up by name with no store or job filter | GAP |
| B-29 | Create POST is wired to `hide.bs.modal` — **Cancel and × also submit** | GAP |
| B-30 | `GetOrCreateStoreItemAsync` matches `StoreId + StoreItemName`; on hit reuses the item and does **not** update price/comments | IMPL |
| B-31 | No sizes and no colours → `CreateDefaultSkuAsync`, one null/null SKU | DONE (already correct) |
| B-32 | New SKUs born `Active = true, MaxCanSell = 0`; no MaxCanSell field at creation | IMPL |
| B-33 | `Item Comments` is commented out of the modal; JS sends `null` | IMPL |
| B-34 | New items born `SortOrder = 0` → sort **last** on the storefront (see A-02) | GAP |
| B-35 | Client validation is `if (itemName && itemPrice)` — sizes/colours **not** enforced despite the placeholder | GAP |
| B-09 | `StoreSkus/Index` grouped by item, collapsible, "N skus" caption | WALKED |
| B-10 | SKU columns: Active · Sku · PickedUp · Sold · UnSold · MaxCanSell · Price | WALKED |
| B-11 | `PickedUp` = `CartBatchSkuItemsSignedFor` | GAP |
| B-12 | `UnSold = MaxCanSell − Sold` (**no in-cart deduction**) | GAP |
| B-13 | SKU label `Item:Size:Color`, `::`→`:` collapse when a dimension is null | UNVER |
| B-14 | SKU sort: Item → Size → Colour, alphabetical | UNVER |
| B-15 | `UpdateSku` writes **Active + MaxCanSell ONLY** | UNVER |
| B-16 | `UpdateSku` StoreItemSkuId==0 branch → updates parent item Active | GAP |
| B-17 | `UpdateSku` batch branch → delete SKUs, then parent item | GAP |
| B-18 | Skus toolbar: Excel Export | GAP |
| B-19 | `StoreImages/Index` grid — Store Item · File · Image | WALKED |
| B-20 | Images toolbar: **Add · Edit · Delete** (only Store screen with create/delete) | GAP |
| B-21 | Upload, auto-numbered `{storeId}-{storeItemId}-{instance}.jpg`, instance = max+1 | GAP |
| B-22 | Replace an existing image | GAP |
| B-23 | Delete an image, with confirm dialog | GAP |
| B-24 | `MAX_IMAGES_PER_ITEM = 10` cap | GAP |
| B-25 | `missing-image.jpg` fallback when an item has none | UNVER |
| B-26 | StoreItemId edit is a dropdown of the job's items | GAP |
| B-27 | Base64 thumbnail preview in the grid | UNVER |

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
| C-14 | `StoreRefunded/Index` grid | UNVER |
| C-15 | `StoreRestocked/Index` grid, `frozenColumns=4` | UNVER |
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
| G-02 | `Enable Store` · `Allow Store Walk-up` · Contact Email · Refund Policy · Pickup Details | UNVER |
| G-03 | `Enable STP` on the Merch tab | GAP |
| G-04 | `Store Sales Tax` / `Store TSIC Rate` — no `%` in label | GAP |
| G-05 | `StoreAdminAdd` — jqGrid roster of Store Admins | UNVER |
| G-06 | Store Admin add / edit / delete, username readonly on edit | UNVER |
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

**D-2 — Store image storage. Table + statics retained; legacy behaviour to be ported onto it.**
Legacy defines *what happens*; it does not define *where bytes live*. `stores.StoreItemImage` +
`statics.teamsportsinfo.com` wins on read cost (legacy enumerates the entire shared image folder
per item per page render), on deploy safety (legacy images live inside the deploy artifact), and
on ordering (legacy encodes order in the filename suffix). **All B-20…B-26 pathways still ship.**
Until they do, the table is a 20-row hand-seeded snapshot with no writer and changing a product
photo requires SQL — strictly worse than legacy. Ship the write pathways in Phase 1.

## Open recommendations

| ID | Recommendation | Status |
|---|---|---|
| R-01 | Sales tax is a **multiplier**, not a percent — remove `÷ 100` from `RecalculateLineItemFees` | OPEN |
| R-02 | Relabel + range-guard the tax field so `6` cannot be entered meaning 6% | OPEN |
| R-03 | Decide whether `Enable STP` belongs on the new store tab | OPEN |
| R-04 | Add a Sort Order control to the items editor | OPEN |
| R-05 | Decide whether post-creation price editing stays (legacy forbids it) | OPEN |
| R-06 | Sort the items list by SortOrder, or offer both | OPEN |
| R-07 | Add `PickedUp` to the SKU panel | OPEN |
| R-08 | Keep legacy `UnSold` as its own column alongside In Cart | OPEN |
| R-09 | Preserve legacy's alphabetical size ordering | OPEN |

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
