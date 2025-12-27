/**
 * Type helper utilities for handling API responses
 * 
 * The backend API returns numeric fields as `number | string` due to JSON serialization.
 * These helpers safely convert mixed types to their expected runtime types.
 */

/**
 * Safely converts a value that might be number | string to a number.
 * Returns 0 for null/undefined/NaN.
 * 
 * @param value - The value to convert
 * @returns A number, or 0 if conversion fails
 * 
 * @example
 * const total = toNumber(response.total); // handles "123" or 123
 * if (toNumber(item.paidTotal) > 0) { ... }
 */
export function toNumber(value: number | string | undefined | null): number {
    if (value === undefined || value === null) return 0;
    return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
}

/**
 * Safely converts a value that might be number | string to a string.
 * Returns empty string for null/undefined.
 * 
 * @param value - The value to convert
 * @returns A string representation
 */
export function toString(value: number | string | undefined | null): string {
    if (value === undefined || value === null) return '';
    return typeof value === 'number' ? value.toString() : value;
}
