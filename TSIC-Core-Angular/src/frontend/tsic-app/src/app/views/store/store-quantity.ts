/**
 * The shopper's per-add quantity ceiling. One definition for both add-to-cart surfaces — the
 * catalog's inline picker and the item detail page.
 *
 * LEGACY: `StoreFamilyController` builds the quantity dropdown from a hard `int maxQuantity = 5`.
 * Five of one variant per add, unconditionally — availability is not consulted, so a shopper
 * could add 5 of something with 2 in stock and only find out at checkout, which is the exact
 * event the Quantity Adjustments log records.
 *
 * We keep the 5 (it is the business rule) and take the LOWER of it and what is actually on the
 * shelf. When availability has not come back yet the ceiling is legacy's 5, not an open field.
 *
 * The IN-CART editor is deliberately uncapped, matching legacy's freely-editable cart grid: 5 is
 * a limit on one add, not on what a family may own.
 */
export const MAX_PER_ADD = 5;

/** The ceiling for one add: legacy's 5, or the shelf, whichever is smaller. */
export function maxAddQuantity(availableCount: number | null | undefined): number {
	return Math.min(MAX_PER_ADD, availableCount ?? MAX_PER_ADD);
}

/** Clamps a requested quantity into 1..maxAddQuantity. */
export function clampAddQuantity(
	requested: number,
	availableCount: number | null | undefined,
): number {
	return Math.max(1, Math.min(requested, maxAddQuantity(availableCount)));
}
