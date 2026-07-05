/**
 * Local help types. Help content is a pure frontend concern now — static HTML fragments under
 * public/help, keyed by route, rendered with the app's own design system. These are the canonical
 * shapes (not backend DTOs), so defining them here is correct, not a duplication of a generated model.
 */
export interface HelpContent {
  readonly component: string;
  readonly topic: string;
  readonly html: string;
  readonly exists: boolean;
}

export interface HelpManifest {
  readonly keys: readonly string[];
}
