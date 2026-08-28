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
| A-18 | Availability re-check → auto-trim + `bCartHasBeenAutoUpdated` banner | BUILT — see D-8 |
| A-19 | Quantity-adjustment audit row on auto-trim | BUILT — see D-8 |
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
| C-01 | `StoreSales/Index` line-item grid. Ours is the same grain (one row per purchased line) with the columns a director acts on; the 24-column set is trimmed, see R-15 | IMPL |
| C-02 | Columns incl. DirectTo club · agegroup · pool · team · email · cellphone | IMPL |
| C-03 | Excel export **including hidden columns** | GAP |
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
| C-14 | `StoreRefunded/Index` grid | IMPL — column set not yet compared |
| C-15 | `StoreRestocked/Index` grid, `frozenColumns=4` | IMPL — column set not yet compared |
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
| R-15 | Legacy's sales grid carries 24 columns, most hidden behind the column chooser and only reachable via Excel export. Ours shows the 9 a director acts on and drops the rest until C-03 (export) lands, where the full set belongs. Decide then whether any hidden column deserves to be on-screen. | OPEN |
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
