/**
 * Shared type definitions for the v2 wizard shell composition pattern.
 *
 * WizardStepDef describes a single step in any wizard.
 * WizardShellConfig provides top-level wizard identity (title, theme, badge).
 */

/** A single step definition fed to the WizardShellComponent. */
export interface WizardStepDef {
    /** Unique identifier used for @switch dispatch and deep-link query param. */
    id: string;
    /** Human-readable label shown in the step indicator. */
    label: string;
    /** When false the step is skipped (conditional steps like eligibility/waivers). */
    enabled: boolean;
}

/** Top-level identity for the wizard shell header. */
export interface WizardShellConfig {
    /** Title displayed in the wizard header (e.g. "Player Registration"). */
    title: string;
    /** Theme key applied as CSS class: wizard-theme-{theme}. */
    theme: 'player' | 'team' | 'family';
    /** Optional badge text next to the title (e.g. family last name). */
    badge?: string | null;
}
