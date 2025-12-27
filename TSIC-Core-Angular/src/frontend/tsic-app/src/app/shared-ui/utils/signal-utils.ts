import { EffectOptions, EffectRef, EnvironmentInjector, effect, runInInjectionContext } from '@angular/core';

/**
 * Safely create an Angular signal effect using a stored EnvironmentInjector.
 *
 * Usage pattern (in a component/service):
 *   private readonly env = inject(EnvironmentInjector);
 *   private readonly myEffect = effectWith(this.env, () => { /* ... *\/ });
 *
 * Or when you need to create an effect later (e.g., in a method):
 *   this.dynamicRef = effectWith(this.env, () => { /* ... *\/ });
 */
export function effectWith(
    env: EnvironmentInjector,
    fn: Parameters<typeof effect>[0],
    options?: EffectOptions
): EffectRef {
    return runInInjectionContext(env, () => effect(fn, options));
}
