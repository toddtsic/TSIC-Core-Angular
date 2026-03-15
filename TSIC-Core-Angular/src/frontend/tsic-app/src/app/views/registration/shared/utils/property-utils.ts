/**
 * Shared wizard utilities for case-insensitive property access and string matching.
 * Eliminates 5+ duplicate property fallback chains and 4+ inline hasAll lambdas.
 */

/**
 * Case-insensitive property accessor for API responses that may use PascalCase or camelCase.
 * Builds a lowercase→actualKey map, then tries each candidate key in order.
 *
 * @example
 * const jrf = getPropertyCI<JobRegFormDto>(resp, 'jobRegForm'); // finds 'jobRegForm' or 'JobRegForm'
 */
export function getPropertyCI<T>(obj: Record<string, unknown> | null | undefined, ...keys: string[]): T | undefined {
    if (!obj || typeof obj !== 'object') return undefined;
    const lowerMap = new Map<string, string>();
    for (const k of Object.keys(obj)) {
        lowerMap.set(k.toLowerCase(), k);
    }
    for (const key of keys) {
        const actualKey = lowerMap.get(key.toLowerCase());
        if (actualKey !== undefined && obj[actualKey] != null) {
            return obj[actualKey] as T;
        }
    }
    return undefined;
}

/**
 * Pick the first non-blank string value from an object using case-insensitive key matching.
 * Replacement for the inline `pick(o, keys)` pattern used in family user normalization.
 *
 * @example
 * const email = pickStringCI(fu, 'email', 'Email', 'parentEmail', 'ParentEmail');
 */
export function pickStringCI(obj: Record<string, unknown> | null | undefined, ...keys: string[]): string | undefined {
    if (!obj || typeof obj !== 'object') return undefined;
    const lowerMap = new Map<string, string>();
    for (const k of Object.keys(obj)) {
        lowerMap.set(k.toLowerCase(), k);
    }
    for (const key of keys) {
        const actualKey = lowerMap.get(key.toLowerCase());
        if (actualKey !== undefined) {
            const v = obj[actualKey];
            if (typeof v === 'string' && v.trim()) return v.trim();
        }
    }
    return undefined;
}

/**
 * Check whether a string contains ALL specified substrings (case-sensitive).
 * Caller should lowercase inputs when case-insensitive matching is needed.
 *
 * @example
 * hasAllParts(fieldName.toLowerCase(), ['grad', 'year']) // true for 'gradyear', 'graduationyear'
 */
export function hasAllParts(str: string, parts: string[]): boolean {
    return parts.every(p => str.includes(p));
}
