/**
 * The one way money is rendered for a human.
 *
 * There were two implementations of this, copied between components:
 *
 *   '$' + value.toFixed(2)                    -- 7 copies
 *   new Intl.NumberFormat('en-US', ...)       -- 2 copies
 *
 * They disagree on the two cases that matter. The hand-rolled one has no thousands
 * separator, so a store's revenue total read "$1159.36"; and it puts the sign in the wrong
 * place, so a refund read "$-12.00" rather than "-$12.00". The store shows both.
 */
export function formatCurrency(value: number): string {
	return value.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
}
