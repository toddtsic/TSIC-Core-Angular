# 018 — Merch Store

> **Status**: Planning
> **Date**: 2026-02-22
> **Legacy reference**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Store/Admin/` (13 controllers)
> **Legacy service**: `IStoreService.cs` (1,644 lines, 45+ methods)
> **Legacy routes**: `StoreDashboard/Index`, `StoreItems/Index`, `StoreFamily/Index`, etc.
> **Planning doc**: `docs/Plans/MERCH-STORE-SYSTEM.md` (schema deep-dive)

---

## How This Migration Differs

**Every other migration plan in this folder ports battle-tested legacy UI to modern Angular.**
**This one does not.** The legacy store UI is new, untested, and not a strong model.

What we trust:
- **The schema** — 12 entities in `stores.*`, production-proven, ER design is solid
- **The business logic flow** — what operations exist, what queries to run, what the data lifecycle looks like
- **The financial model** — independent ledger, multi-fee pricing, payment processor integration

What we do NOT trust:
- **The legacy UI** — treat as prototype only, not as a migration target
- **The UX patterns** — design from scratch using our established design system

This means the frontend is **original design work**, not translation. The backend faithfully implements the schema's data flows; the frontend brings fresh UX.

---

## 1. Problem Statement

Jobs can sell merchandise — jerseys, equipment, team gear. Purchases happen in two contexts:

1. **Walk-up** — standalone storefront, no registration linkage (event-day sales, non-registration sites)
2. **Registration-linked** — merch bundled with player registration, or "second-chance" purchase links sent post-registration

Directors need admin tools to configure their store catalog, manage inventory, process orders, handle refunds, and view sales analytics.

---

## 2. Schema Summary

### Independent Financial Ledger

The store runs its own accounting, completely separate from `RegistrationAccounting`:

```
Registration world:  Registrations → RegistrationAccounting
Store world:         StoreCartBatches → StoreCartBatchAccounting
                                      → StoreCartBatchSkus (per-line-item financials)
```

The only bridge: `StoreCartBatchSkus.DirectToRegId` — a reference link, not a financial merge.

### Entity Hierarchy (12 entities)

```
Stores (per-job, supports hierarchy)
 ├── StoreItems → StoreItemSkus (Color × Size variant matrix)
 │                 ├── StoreColors (lookup)
 │                 └── StoreSizes (lookup)
 │
 └── StoreCart (per FamilyUserId)
      └── StoreCartBatches (orders)
           ├── StoreCartBatchSkus (line items + full financial breakdown)
           │    ├── StoreCartBatchSkuEdits (version history)
           │    └── StoreCartBatchSkuRestocks (restock records)
           ├── StoreCartBatchAccounting (payments)
           └── StoreCartBatchSkuQuantityAdjustments (inventory deltas)
```

### Per-Line-Item Financial Fields

| Field | Purpose |
|---|---|
| `UnitPrice` | Base price per unit |
| `FeeProduct` | Product fee |
| `FeeProcessing` | Processing fee |
| `SalesTax` | Tax |
| `FeeTotal` | Total fees |
| `PaidTotal` | Amount paid |
| `RefundedTotal` | Amount refunded |
| `Quantity` | Units ordered |
| `Restocked` | Units returned to inventory |

### Job-Level Store Config

`BEnableStore`, `StoreSalesTax`, `StoreRefundPolicy`, `StorePickupDetails`, `StoreContactEmail`, `StoreTsicrate` — already exposed via `MobileStoreTabComponent` in Job Config Editor.

### SKU Matrix Generation

| Sizes? | Colors? | SKUs created |
|---|---|---|
| Yes | Yes | Size × Color combinations |
| Yes | No | One per size |
| No | Yes | One per color |
| No | No | One default (no variant) |

---

## 3. Dev Order

### Phase 1: Backend (Repos + Services + Controller)

Build the full data access and API layer. Everything else depends on this.

#### Repositories needed

| Repository | Covers |
|---|---|
| `IStoreRepository` | `Stores` — per-job store CRUD |
| `IStoreItemRepository` | `StoreItems` + `StoreItemSkus` — catalog with variant matrix |
| `IStoreLookupRepository` | `StoreColors` + `StoreSizes` — shared lookups |
| `IStoreCartRepository` | `StoreCart` + `StoreCartBatches` + `StoreCartBatchSkus` — cart/order pipeline |
| `IStoreAccountingRepository` | `StoreCartBatchAccounting` — payments |
| `IStoreInventoryRepository` | `StoreCartBatchSkuQuantityAdjustments` + `StoreCartBatchSkuRestocks` + `StoreCartBatchSkuEdits` — audit trail |

#### Services needed

| Service | Responsibility |
|---|---|
| `IStoreCatalogService` | Item CRUD, SKU matrix generation, image management |
| `IStoreCartService` | Cart operations, checkout flow, order management |
| `IStoreAccountingService` | Payment recording, refund processing, financial queries |
| `IStoreAnalyticsService` | Sales reporting, pivot data, dashboards |

#### Controller

Single `StoreController` with route groups — no need to replicate the 13-controller legacy sprawl.

### Phase 2: Walk-Up Storefront (Customer-Facing)

The standalone purchase path — proves the full stack works end-to-end.

**Route**: `/:jobPath/store`

**Flow**: Browse catalog → pick variants → add to cart → checkout → payment → confirmation

**Key UX decisions** (to be designed fresh):
- Product cards with variant pickers (color/size dropdowns or visual selectors)
- Availability display (MaxCanSell minus sold)
- Cart as slide-out panel or dedicated page
- Checkout with refund policy + pickup details display
- Order confirmation with store contact info

### Phase 3: Store Admin

**Route**: `/:jobPath/admin/store`

**Tabs or sections** (TBD — design from scratch):
- Catalog management (items, SKUs, images)
- Order management (view, refund, restock)
- Sales analytics (charts, pivot data)
- Inventory overview (stock levels, adjustments)

### Phase 4: Registration Integration

The thin layer on top of a working walk-up:
- Embed store selection in registration wizard
- Set `DirectToRegId` on line items
- "Second-chance" purchase links post-registration
- This should be relatively simple once walk-up is solid

---

## 4. Existing Backend Infrastructure

| Layer | Status |
|---|---|
| DB schema (`stores.*`) | Production — 12 tables |
| EF entities | Fully scaffolded in `TSIC.Domain` |
| DbContext mappings | Configured (SqlDbContext.cs lines 5829-6182) |
| Job config fields | In `Jobs` table + `JobConfigMobileStoreDto` |
| Role constant | `RoleConstants.StoreAdmin` defined |
| Config UI | `MobileStoreTabComponent` in Job Config Editor |
| **Repositories** | **None — to be created** |
| **Services** | **None — to be created** |
| **Controllers** | **None — to be created** |
| **Store frontend** | **None — to be created** |

---

## 5. Legacy Reference (Business Logic Only)

The legacy codebase is reference for **data operations**, not UI:

| Legacy Controller | What to extract |
|---|---|
| `StoreItemsController` | Item + SKU creation logic, matrix generation |
| `StoreSkusController` | SKU activation, MaxCanSell updates, batch deletion |
| `StoreImagesController` | Image upload/delete/renumber logic, file naming convention |
| `StoreDashboardController` | Analytics query patterns (pivot, pie, time-series) |
| `StoreSalesController` | Sales query with player/team joins |
| `StoreSalesWalkupController` | Walk-up specific filtering |
| `StoreRefundedController` | Refund data shape |
| `StoreRestockedController` | Restock recording logic |
| `StoreFamilyController` | Cart operations, checkout, payment recording, PDF invoices |
| `StoreCartQuantityAdjustmentsController` | Adjustment logging |
| Email controllers (3) | Email campaign query patterns |

---

## 6. Open Questions

1. **Store hierarchy**: `ParentStoreId` self-ref — used in practice or vestigial?
2. **Payment processor**: ADN still active, or has the processor changed?
3. **Image storage**: Use `FileStorage` pattern (like branding) or different path?
4. **Walk-up auth**: Must be authenticated family user, or can anonymous users buy?
5. **Chart library**: Chart.js (like widget dashboard) for analytics?
6. **Mobile admin**: Directors using tablets at events for walk-up — is tablet-friendly admin a priority?
7. **Email campaigns**: Port the 3 email controllers now or defer to Phase 5?

---

## 7. Files (Projected)

### Backend — Phase 1

| File | Layer |
|---|---|
| `TSIC.Contracts/Repositories/IStoreRepository.cs` | Interface |
| `TSIC.Contracts/Repositories/IStoreItemRepository.cs` | Interface |
| `TSIC.Contracts/Repositories/IStoreLookupRepository.cs` | Interface |
| `TSIC.Contracts/Repositories/IStoreCartRepository.cs` | Interface |
| `TSIC.Contracts/Repositories/IStoreAccountingRepository.cs` | Interface |
| `TSIC.Contracts/Repositories/IStoreInventoryRepository.cs` | Interface |
| `TSIC.Infrastructure/Repositories/StoreRepository.cs` | Implementation |
| `TSIC.Infrastructure/Repositories/StoreItemRepository.cs` | Implementation |
| `TSIC.Infrastructure/Repositories/StoreLookupRepository.cs` | Implementation |
| `TSIC.Infrastructure/Repositories/StoreCartRepository.cs` | Implementation |
| `TSIC.Infrastructure/Repositories/StoreAccountingRepository.cs` | Implementation |
| `TSIC.Infrastructure/Repositories/StoreInventoryRepository.cs` | Implementation |
| `TSIC.Contracts/Dtos/Store/StoreDtos.cs` | DTOs |
| `TSIC.Application/Services/IStoreCatalogService.cs` | Interface |
| `TSIC.Application/Services/IStoreCartService.cs` | Interface |
| `TSIC.Application/Services/IStoreAccountingService.cs` | Interface |
| `TSIC.Application/Services/IStoreAnalyticsService.cs` | Interface |
| `TSIC.API/Services/StoreCatalogService.cs` | Implementation |
| `TSIC.API/Services/StoreCartService.cs` | Implementation |
| `TSIC.API/Services/StoreAccountingService.cs` | Implementation |
| `TSIC.API/Services/StoreAnalyticsService.cs` | Implementation |
| `TSIC.API/Controllers/StoreController.cs` | API endpoints |

### Frontend — Phase 2 (Walk-Up)

| File | Layer |
|---|---|
| `views/store/storefront/storefront.component.ts` | Customer storefront |
| `views/store/storefront/storefront.component.html` | Template |
| `views/store/storefront/storefront.component.scss` | Styles |
| `views/store/cart/store-cart.component.ts` | Cart (panel or page TBD) |
| `views/store/checkout/store-checkout.component.ts` | Checkout + payment |
| `views/store/confirmation/store-confirmation.component.ts` | Order confirmation |

### Frontend — Phase 3 (Admin)

| File | Layer |
|---|---|
| `views/admin/store-admin/store-admin.component.ts` | Admin shell |
| `views/admin/store-admin/catalog-tab/catalog-tab.component.ts` | Item/SKU management |
| `views/admin/store-admin/orders-tab/orders-tab.component.ts` | Order management |
| `views/admin/store-admin/analytics-tab/analytics-tab.component.ts` | Sales dashboard |
| `views/admin/store-admin/inventory-tab/inventory-tab.component.ts` | Stock overview |

### Routes

| Route | Purpose |
|---|---|
| `/:jobPath/store` | Walk-up storefront |
| `/:jobPath/admin/store` | Store admin |
| `/:jobPath/storedashboard/index` | Legacy redirect → admin/store |
| `/:jobPath/storeitems/index` | Legacy redirect → admin/store |
| `/:jobPath/storefamily/index` | Legacy redirect → store |
