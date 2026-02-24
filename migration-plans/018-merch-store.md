# 018 — Merch Store

> **Status**: In Progress (Phase 1-2 substantially complete)
> **Date**: 2026-02-24
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

1. **Walk-up** — standalone storefront, lightweight POS registration (event-day sales at the merch table)
2. **Registration-linked** — merch bundled with player registration, or "second-chance" purchase links sent post-registration

Directors need admin tools to configure their store catalog, manage inventory, process orders, handle refunds, and view sales analytics.

---

## 2. Architecture — Three Store Surfaces

The store has **three distinct entry points** that share common shopping UI components:

### Surface 1: Admin (Catalog Management)
- **Route**: `/:jobPath/admin/store`
- **Auth**: `StoreAdmin` policy (Superuser, Director, or Store Admin role)
- **Purpose**: Item/SKU CRUD, color/size management, analytics, order management
- **Status**: BUILT — `StoreAdminComponent` with items/colors/sizes/analytics tabs

### Surface 2: Authenticated Shopper
- **Route**: `/:jobPath/store`
- **Auth**: `authGuard` (requires JWT — registered family user)
- **Purpose**: Browse catalog, select variants, add to cart, checkout
- **Status**: BUILT — catalog, item detail, cart, checkout components

### Surface 3: Walk-Up POS
- **Route**: `/:jobPath/walk-up` (TBD)
- **Auth**: `[AllowAnonymous]` entry point → lightweight mini-registration form
- **Purpose**: Event-day merch table sales — collect name/email/phone/address, then shop
- **Status**: NOT BUILT
- **Legacy reference**: `StoreFamilyController.Index` had `[AllowAnonymous]`, rendered `StoreWalkUpRegistrationDto` with contact form (First Name, Last Name, Email, Phone, Address, City, State, ZIP)

### Shared Shopping UI (not a surface — a component library)

The catalog, item detail, cart, and checkout components are **shared UI** consumed by surfaces 2 and 3. They don't know or care who the buyer is — they just need:
- A store context (resolved from jobPath)
- A buyer identity (full JWT registration OR lightweight walk-up identity)

```
Surface 2 (auth guard) ──┐
                         ├──→ Shared store components (catalog → detail → cart → checkout)
Surface 3 (walk-up form)─┘
```

The difference between surfaces is the **entry point** and **identity context**, not the shopping experience.

---

## 3. Schema Summary

### Independent Financial Ledger

The store runs its own accounting, completely separate from `RegistrationAccounting`:

```
Registration world:  Registrations → RegistrationAccounting
Store world:         StoreCartBatches → StoreCartBatchAccounting
                                      → StoreCartBatchSkus (per-line-item financials)
```

The only bridge: `StoreCartBatchSkus.DirectToRegId` — a reference link, not a financial merge.

### Entity Hierarchy (13 entities)

```
Stores (per-job, supports hierarchy)
 ├── StoreItems → StoreItemSkus (Color × Size variant matrix)
 │    ├── StoreItemImage (per-item image gallery, ordered)   ← NEW
 │    ├── StoreColors (lookup)
 │    └── StoreSizes (lookup)
 │
 └── StoreCart (per FamilyUserId)
      └── StoreCartBatches (orders)
           ├── StoreCartBatchSkus (line items + full financial breakdown)
           │    ├── StoreCartBatchSkuEdits (version history)
           │    └── StoreCartBatchSkuRestocks (restock records)
           ├── StoreCartBatchAccounting (payments)
           └── StoreCartBatchSkuQuantityAdjustments (inventory deltas)
```

### Image Storage (RESOLVED)

- **Table**: `stores.StoreItemImage` — one row per image, with `ImageUrl` (full URL), `DisplayOrder`, `AltText`
- **Physical storage**: Statics CDN at `https://statics.teamsportsinfo.com/Store-Sku-Images/`
- **Legacy naming**: `{storeId}-{storeItemId}-{instance}.jpg` — but note StoreItemId is globally unique (identity), so StoreId in filename doesn't always match actual StoreId
- **Architecture**: URLs stored as data in DB, not derived from conventions. No in-code URL generation.
- **Why not store images in DB**: SQL Server isn't a CDN — blob storage causes DB size explosion, bandwidth costs, and defeats existing CDN infrastructure

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

## 4. Dev Order & Progress

### Phase 1: Backend — COMPLETE

| Layer | Status | Details |
|---|---|---|
| `IStoreRepository` | Done | Store + color/size CRUD, in-use checks |
| `IStoreItemRepository` | Done | Items + SKUs with availability, image JOIN |
| `IStoreCartRepository` | Done | Cart operations, batch management |
| `StoreCatalogService` | Done | Item CRUD, SKU matrix, color/size management |
| `StoreCartService` | Done | Cart ops, checkout flow |
| `StoreAdminService` | Done | Analytics, restock, pickup signing |
| `StoreController` | Done | Single controller, route-grouped, policy-protected |
| `StoreItemImage` table | Done | Created, seeded with legacy images |
| DTOs | Done | `StoreCatalogDtos.cs` with `ImageUrls` from table JOIN |

### Phase 2: Customer Shopping UI — MOSTLY COMPLETE

| Component | Status | Notes |
|---|---|---|
| `store-catalog` | Done | Grid cards with product images, fallback placeholder |
| `store-item-detail` | Done | Hero image + thumbnail strip, variant picker, availability, add-to-cart |
| `store-cart` | Done | Cart page with quantity controls |
| `store-checkout` | Done | Payment + refund policy display |
| Product images (display) | Done | DB-backed `StoreItemImage` → `ImageUrls` in DTOs → all 3 surfaces |
| Image upload (admin) | NOT STARTED | Backend endpoint + admin UI needed |

### Phase 3: Store Admin — MOSTLY COMPLETE

| Component | Status | Notes |
|---|---|---|
| `store-admin` (shell) | Done | Tabs: items, colors, sizes, analytics |
| Item CRUD | Done | Create with SKU matrix, edit, toggle active |
| SKU management | Done | Expand item → edit SKU (active, maxCanSell) |
| Color/Size CRUD | Done | Add, edit, delete with in-use protection |
| Analytics tab | Done | Sales data display |
| Item thumbnails | Done | First image shown next to item name |
| Image upload UI | NOT STARTED | Upload/reorder/delete per item |
| Order management | NOT STARTED | View orders, process refunds, restock |

### Phase 4: Walk-Up POS — NOT STARTED

The lightweight anonymous entry point:
- Mini-registration form (name, email, phone, address)
- Backend endpoint to create walk-up identity without full auth
- Routes into same shared store components
- Legacy reference: `StoreFamilyController.Index` with `[AllowAnonymous]`

### Phase 5: Registration Integration — NOT STARTED

- Embed store selection in registration wizard
- Set `DirectToRegId` on line items
- "Second-chance" purchase links post-registration

---

## 5. Existing Infrastructure

| Layer | Status |
|---|---|
| DB schema (`stores.*`) | Production — 13 tables (incl. StoreItemImage) |
| EF entities | Fully scaffolded in `TSIC.Domain` |
| DbContext mappings | Configured (SqlDbContext.cs) |
| Job config fields | In `Jobs` table + `JobConfigMobileStoreDto` |
| Role constant | `RoleConstants.StoreAdmin` defined |
| Config UI | `MobileStoreTabComponent` in Job Config Editor |
| Repositories | `IStoreRepository`, `IStoreItemRepository` + implementations |
| Services | `StoreCatalogService`, `StoreCartService`, `StoreAdminService` |
| Controller | `StoreController` — single unified controller |
| Frontend service | `store.service.ts` — full HTTP wrapper with cart signals |
| Frontend components | Catalog, detail, cart, checkout, admin (all built) |

---

## 6. Legacy Reference (Business Logic Only)

The legacy codebase is reference for **data operations**, not UI:

| Legacy Controller | What to extract | Status |
|---|---|---|
| `StoreItemsController` | Item + SKU creation logic, matrix generation | Extracted |
| `StoreSkusController` | SKU activation, MaxCanSell updates, batch deletion | Extracted |
| `StoreImagesController` | Image upload/delete/renumber logic | Partially — display done, upload TBD |
| `StoreDashboardController` | Analytics query patterns (pivot, pie, time-series) | Extracted |
| `StoreSalesController` | Sales query with player/team joins | Extracted |
| `StoreSalesWalkupController` | Walk-up specific filtering | Not started |
| `StoreRefundedController` | Refund data shape | Not started |
| `StoreRestockedController` | Restock recording logic | Extracted |
| `StoreFamilyController` | Cart operations, checkout, payment, walk-up registration | Cart done, walk-up TBD |
| `StoreCartQuantityAdjustmentsController` | Adjustment logging | Not started |
| Email controllers (3) | Email campaign query patterns | Not started |
| `StoreAdminAddController` | Admin POS mode (add to cart on behalf) | Not started |

---

## 7. Open Questions (Updated)

1. ~~**Store hierarchy**: `ParentStoreId` self-ref — used in practice or vestigial?~~
2. ~~**Payment processor**: ADN still active, or has the processor changed?~~
3. ~~**Image storage**: Use `FileStorage` pattern (like branding) or different path?~~ **RESOLVED** — `StoreItemImage` table with CDN URLs
4. ~~**Walk-up auth**: Must be authenticated family user, or can anonymous users buy?~~ **RESOLVED** — Walk-up is anonymous entry + lightweight mini-registration form, then shared shopping flow
5. ~~**Chart library**: Chart.js (like widget dashboard) for analytics?~~
6. **Mobile admin**: Directors using tablets at events for walk-up — is tablet-friendly admin a priority?
7. **Email campaigns**: Port the 3 email controllers now or defer?
8. **Image upload**: Reuse branding's `IJobImageService` pattern or create store-specific `IStoreImageService`?

---

## 8. Routes

| Route | Surface | Auth | Status |
|---|---|---|---|
| `/:jobPath/admin/store` | Admin | StoreAdmin policy | Built |
| `/:jobPath/store` | Authenticated shopper | authGuard (JWT) | Built |
| `/:jobPath/store/item/:storeItemId` | Item detail | authGuard (JWT) | Built |
| `/:jobPath/store/cart` | Cart | authGuard (JWT) | Built |
| `/:jobPath/store/checkout` | Checkout | authGuard (JWT) | Built |
| `/:jobPath/walk-up` | Walk-up POS | AllowAnonymous → mini-form | NOT BUILT |
| `/:jobPath/storedashboard/index` | Legacy redirect | → admin/store | TBD |
| `/:jobPath/storeitems/index` | Legacy redirect | → admin/store | TBD |
| `/:jobPath/storefamily/index` | Legacy redirect | → walk-up | TBD |

---

## 9. Scripts

| Script | Purpose | Status |
|---|---|---|
| `scripts/store-item-images-setup.sql` | CREATE TABLE + seed legacy images | Run on dev + prod |
| `scripts/seed-store-image-counts.sql` | OBSOLETE — old ImageCount approach | Delete |
