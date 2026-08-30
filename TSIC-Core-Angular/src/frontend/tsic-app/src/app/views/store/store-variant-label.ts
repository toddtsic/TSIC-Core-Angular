import { isPlaceholderVariantName } from './store-size-order';

/**
 * "Gray · Adult Large" — how one SKU's variant reads on every PURCHASER-facing store screen.
 *
 * <p>This existed three times over, copied into the catalog, the cart and the checkout, and had
 * already drifted: all three joined with <c>' / '</c> while the storefront card and the
 * dialog's unavailable list used <c>' · '</c>, so one flow showed the same kind of information
 * three different ways. One function, one separator.</p>
 *
 * <p>Placeholder names are dropped. Item create seeds a single default SKU with "Standard" on
 * both dimensions, so a Sticker line read "Standard / Standard" — two words answering nothing.
 * Callers must therefore handle an EMPTY string: it means the product has no variants worth
 * naming, and the item name alone is the whole answer.</p>
 *
 * <p>Colour before size, matching the order the picker asks for them.</p>
 *
 * <p>Deliberately not used by the store ADMIN grids. Those label a SKU for someone managing
 * stock, where "Standard" is a real row that must stay visible and legacy's own
 * <c>SkuLabel</c> shape governs.</p>
 */
export function variantLabel(
    item: { colorName?: string | null; sizeName?: string | null }
): string {
    const parts: string[] = [];
    if (item.colorName && !isPlaceholderVariantName(item.colorName)) parts.push(item.colorName);
    if (item.sizeName && !isPlaceholderVariantName(item.sizeName)) parts.push(item.sizeName);
    return parts.join(' · ');
}
