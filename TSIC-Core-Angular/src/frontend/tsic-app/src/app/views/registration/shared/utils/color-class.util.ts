/** Deterministic background color per player index (light/dark friendly using *-subtle variants). */
export function colorClassForIndex(idx: number): string {
  const palette = [
    'bg-primary-subtle border-primary-subtle',
    'bg-success-subtle border-success-subtle',
    'bg-info-subtle border-info-subtle',
    'bg-warning-subtle border-warning-subtle',
    'bg-secondary-subtle border-secondary-subtle',
  ];
  return palette[idx % palette.length];
}

/** Coordinated text badge color per player index. */
export function textColorClassForIndex(idx: number): string {
  const palette = [
    'bg-primary-subtle text-primary-emphasis border border-primary-subtle',
    'bg-success-subtle text-success-emphasis border border-success-subtle',
    'bg-info-subtle text-info-emphasis border border-info-subtle',
    'bg-warning-subtle text-warning-emphasis border border-warning-subtle',
    'bg-secondary-subtle text-secondary-emphasis border border-secondary-subtle',
  ];
  return palette[idx % palette.length];
}
