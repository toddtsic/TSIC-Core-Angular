# Merch / Store System — Planning Document

> **Status**: Early Planning (Schema Analysis Complete)
> **Date**: 2026-02-22
> **Scope**: Full e-commerce module per job — catalog, cart, checkout, admin, analytics

---

## 1. Domain Overview

The Store/Merch system is a **self-contained e-commerce module** embedded per-job. It operates two independent purchase paths:

1. **Registration-linked** — merch purchased during player/team registration (`DirectToRegId` set)
2. **Walk-up** — standalone purchases with no registration linkage (`DirectToRegId = NULL`)

**Critical architectural insight**: The store maintains its **own independent financial ledger**, completely separate from `RegistrationAccounting`. Store payments and registration payments never co-mingle. The only bridge is `StoreCartBatchSkus.DirectToRegId` — a reference link, not a financial merge.

---

## 2. Database Schema (`stores.*`)

### 2.1 Entity Hierarchy

```
Jobs (1)
 └── Stores (per-job store, supports parent-child hierarchy via ParentStoreId)
      ├── StoreItems (catalog — name, price, comments, active, sortOrder)
      │    └── StoreItemSkus (variants — Color × Size matrix, MaxCanSell inventory cap)
      │         ├── StoreColors (lookup — "Royal Blue", "Red", etc.)
      │         └── StoreSizes (lookup — "S", "M", "L", "XL", etc.)
      │
      └── StoreCart (per-customer shopping cart, keyed by FamilyUserId)
           └── StoreCartBatches (orders — with SignedForDate/SignedForBy for pickup)
                ├── StoreCartBatchSkus (line items — the financial core)
                │    ├── → StoreItemSkus (what was bought)
                │    ├── → Registrations (optional DirectToRegId)
                │    ├── StoreCartBatchSkuEdits (version history of qty changes)
                │    └── StoreCartBatchSkuRestocks (restock records)
                ├── StoreCartBatchAccounting (payments — CC last4, ADN processor IDs, discount codes)
                └── StoreCartBatchSkuQuantityAdjustments (inventory delta tracking)
```

### 2.2 Entity Count: 12

| Entity | Purpose |
|---|---|
| `Stores` | Per-job store root (supports hierarchy via ParentStoreId) |
| `StoreItems` | Catalog products (name, price, comments, active, sortOrder) |
| `StoreItemSkus` | Product variants (Color × Size matrix, MaxCanSell) |
| `StoreColors` | Color lookup table |
| `StoreSizes` | Size lookup table |
| `StoreCart` | Per-customer cart (keyed by FamilyUserId + StoreId) |
| `StoreCartBatches` | Orders (SignedForDate/By for pickup confirmation) |
| `StoreCartBatchSkus` | Order line items (full financial breakdown per item) |
| `StoreCartBatchAccounting` | Payment records (CC, processor IDs, discounts) |
| `StoreCartBatchSkuEdits` | Line item edit history (previous ↔ current versioning) |
| `StoreCartBatchSkuRestocks` | Restock records per line item |
| `StoreCartBatchSkuQuantityAdjustments` | Inventory delta audit trail |

### 2.3 Financial Model (Per Line Item)

Each `StoreCartBatchSkus` row carries:

| Field | Purpose |
|---|---|
| `UnitPrice` | Base price per unit |
| `FeeProduct` | Product fee component |
| `FeeProcessing` | Processing fee component |
| `SalesTax` | Tax charged |
| `FeeTotal` | Total fees (product + processing) |
| `PaidTotal` | Amount paid |
| `RefundedTotal` | Amount refunded |
| `Quantity` | Units ordered |
| `Restocked` | Units returned to inventory |

### 2.4 Job-Level Store Config (in `Jobs` table)

| Field | Purpose |
|---|---|
| `BEnableStore` | Master on/off toggle |
| `StoreSalesTax` | Tax rate (%) |
| `StoreRefundPolicy` | Refund policy text (HTML) |
| `StorePickupDetails` | Pickup instructions (HTML) |
| `StoreContactEmail` | Support email |
| `StoreTsicrate` | TSIC processing fee rate (%) |

### 2.5 FK Graph

```
Stores ──┬── JobId ──────────→ Jobs
         ├── LebUserId ──────→ AspNetUsers (store admin)
         └── ParentStoreId ──→ Stores (self-ref hierarchy)

StoreItems ──┬── StoreId ────→ Stores
             └── LebUserId ──→ AspNetUsers

StoreItemSkus ──┬── StoreItemId ──→ StoreItems
                ├── StoreColorId ──→ StoreColors (optional)
                └── StoreSizeId ──→ StoreSizes (optional)

StoreCart ──┬── StoreId ───────→ Stores
            └── FamilyUserId ──→ AspNetUsers (customer)

StoreCartBatches ──── StoreCartId ──→ StoreCart

StoreCartBatchSkus ──┬── StoreCartBatchId ──→ StoreCartBatches
                     ├── StoreSkuId ────────→ StoreItemSkus
                     └── DirectToRegId ─────→ Registrations (optional)

StoreCartBatchAccounting ──┬── StoreCartBatchId ──→ StoreCartBatches
                           ├── PaymentMethodId ──→ AccountingPaymentMethods
                           └── DiscountCodeAi ──→ JobDiscountCodes (optional)

StoreCartBatchSkuEdits ──┬── StoreCartBatchSkuId ─────────→ StoreCartBatchSkus (current)
                         └── PreviousStoreCartBatchSkuId ──→ StoreCartBatchSkus (previous)

StoreCartBatchSkuQuantityAdjustments ──┬── StoreCartId ──→ StoreCart
                                       └── StoreSkuId ──→ StoreItemSkus

StoreCartBatchSkuRestocks ──── StoreCartBatchSkuId ──→ StoreCartBatchSkus
```

### 2.6 Design Patterns in Schema

- **Audit trail**: Every table has `LebUserId` + `Modified`
- **Soft deletes**: `Active` bool on Items and SKUs
- **Inventory control**: `MaxCanSell` per SKU, adjustment + restock history
- **Multi-fee pricing**: Product fee, processing fee, sales tax tracked separately
- **Edit versioning**: `StoreCartBatchSkuEdits` links current ↔ previous versions
- **Store hierarchy**: `ParentStoreId` self-ref on Stores (unused? needs investigation)

---

## 3. SKU Matrix Generation

When creating a new item, the system auto-generates SKUs:

| Has Sizes | Has Colors | Result |
|---|---|---|
| Yes | Yes | One SKU per Size × Color combination |
| Yes | No | One SKU per Size |
| No | Yes | One SKU per Color |
| No | No | One default SKU (no variant) |

All wrapped in a transaction with rollback on failure.

---

## 4. Legacy Admin Surface (13 Controllers)

**Location**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Store/Admin/`
**Legacy Service**: `IStoreService` — 1,644 lines, 45+ methods

### 4.1 StoreDashboard/Index — Analytics Hub

Three visualizations:
- **Sales pivot table** — units & revenue by product, grouped by year-month
- **Sales pie chart** — percentage breakdown by product
- **Time-series details** — cart batch SKU trends

### 4.2 Full Admin Controller Set

| Controller | Function |
|---|---|
| `StoreDashboardController` | Analytics & reporting hub (3 chart types) |
| `StoreItemsController` | CRUD for merchandise items |
| `StoreSkusController` | Manage size×color variants, toggle active, set MaxCanSell |
| `StoreImagesController` | Up to 10 JPEGs per item, disk-based, auto-renumbering |
| `StoreAdminAddController` | Admin add functionality |
| `StoreSalesController` | Transaction reporting |
| `StoreSalesWalkupController` | Walk-up specific sales tracking |
| `StoreRefundedController` | Refund management & history |
| `StoreRestockedController` | Inventory restock logging |
| `StoreCartQuantityAdjustmentsController` | Inventory change audit |
| `StoreEmailAbandonedCartsController` | Cart recovery email campaigns |
| `StoreEmailFamiliesThatNeverUsedController` | Marketing to non-purchasers |
| `StoreEmailFamiliesThatOrderedController` | Follow-up to purchasers |

### 4.3 Customer-Facing Controller

| Controller | Function |
|---|---|
| `StoreFamilyController` | Storefront — cart, checkout, invoices, payment processing |

---

## 5. Current Angular State

### What Exists

| Component | Location | Purpose |
|---|---|---|
| `MobileStoreTabComponent` | Job Config Editor | Store settings (enable, tax, refund policy, pickup, TSIC rate) |
| EF entities (12) | `TSIC.Domain/Entities/Store*.cs` | Fully scaffolded |
| DbContext mappings | `SqlDbContext.cs` lines 5829-6182 | Configured |
| Role constant | `RoleConstants.StoreAdmin` | Defined |
| Job config DTO | `JobConfigMobileStoreDto` | Exists |

### What Does NOT Exist

- **No Store repositories** (zero)
- **No Store services** (zero)
- **No Store controllers** (zero)
- **No Store frontend components** (zero, beyond config tab)

---

## 6. Proposed Build Order

### Phase 1: Walk-Up Storefront (Customer-Facing)

The simplest starting point — standalone purchase with no registration linkage.

**Why start here**:
- Self-contained (no wizard integration)
- `DirectToRegId` stays null (simpler)
- Exercises the core catalog → cart → checkout → payment pipeline
- Proves out the full stack before adding complexity

**Scope**:
- Browse catalog (items with color/size variant pickers)
- Check availability (MaxCanSell minus sold)
- Add to cart, update quantities, remove items
- Checkout with payment (store's own accounting pipeline)
- Order confirmation with pickup details

### Phase 2: Store Admin — Catalog Management

- Item CRUD (name, price, comments, active, sortOrder)
- SKU matrix generation (sizes × colors)
- Image management
- Inventory settings (MaxCanSell per SKU)

### Phase 3: Store Admin — Sales & Analytics

- Sales dashboard (pivot, pie, time-series)
- Order management
- Refund processing
- Restock logging

### Phase 4: Registration-Linked Purchases

- Embed store in registration wizard flow
- Set `DirectToRegId` on line items
- "Second-chance" purchase links post-registration

### Phase 5: Email Campaigns & Advanced

- Abandoned cart recovery emails
- Customer marketing emails
- PDF invoice generation

---

## 7. Open Questions

1. **Store hierarchy**: `ParentStoreId` self-ref — is this used in practice? What's the use case?
2. **Payment processor**: ADN integration — is this still the active processor, or has it changed?
3. **Image storage**: Legacy used `wwwroot/images/store-sku-images/` — new system should use the same `FileStorage` pattern as branding images?
4. **Walk-up auth**: Does the walk-up customer need to be a registered family user, or can anonymous users purchase?
5. **Syncfusion charts**: Legacy dashboard used Syncfusion PivotView — are we using Chart.js (like widget dashboard) or something else?

---

**Last Updated**: 2026-02-22
