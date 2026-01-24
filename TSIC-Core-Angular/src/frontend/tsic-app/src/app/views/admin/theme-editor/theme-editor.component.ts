import { Component, OnInit, effect, inject, signal } from '@angular/core';

import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { WizardThemeDirective } from '@shared-ui/directives/wizard-theme.directive';

type ThemeKey = 'landing' | 'login' | 'role-select' | 'player' | 'family';

/** Admin-only Theme Editor
 * Route: /:jobPath/admin/theme
 * Allows setting CSS variable-based colors for wizard themes (player/family) per job.
 * Persistence: localStorage (per-browser) under key tsic:theme:{jobPath}:{theme}
 * Application: injects a <style> tag that scopes overrides to .wizard-theme-<name> classes.
 */
@Component({
  selector: 'app-theme-editor',
  standalone: true,
  imports: [ReactiveFormsModule, WizardThemeDirective],
  template: `
  <div class="container py-4">
    <div class="row g-4">
      <div class="col-12">
        <div class="card shadow-sm border-0 card-rounded">
          <div class="card-header card-header-subtle border-0 py-3">
            <h5 class="mb-0 fw-semibold">Theme Editor</h5>
          </div>
          <div class="card-body">
            <div class="row g-3 align-items-end">
              <div class="col-12 col-md-3">
                <label class="form-label">Theme</label>
                <select class="form-select" [formControl]="form.controls['theme']">
                  <option value="landing">Landing</option>
                  <option value="login">Login</option>
                  <option value="role-select">Role Selection</option>
                  <option value="player">Player</option>
                  <option value="family">Family</option>
                </select>
              </div>
              <div class="col-12 col-md-3">
                <label class="form-label">Primary</label>
                <select class="form-select" [formControl]="form.controls['primaryToken']">
                  @for (t of paletteTokens; track t) {
                    <option [value]="t.key">{{t.label}}</option>
                  }
                </select>
                <div class="form-text d-flex align-items-center gap-2 mt-1">
                  <span class="d-inline-block rounded" style="width:18px;height:18px;" [style.background]="'var(' + form.controls['primaryToken'].value + ')'"> </span>
                  <span class="text-muted small">Preview</span>
                </div>
              </div>
              <div class="col-6 col-md-3">
                <label class="form-label">Gradient Start</label>
                <select class="form-select" [formControl]="form.controls['gradientStartToken']">
                  @for (t of paletteTokens; track t) {
                    <option [value]="t.key">{{t.label}}</option>
                  }
                </select>
                <div class="form-text d-flex align-items-center gap-2 mt-1">
                  <span class="d-inline-block rounded" style="width:18px;height:18px;" [style.background]="'var(' + form.controls['gradientStartToken'].value + ')'"> </span>
                </div>
              </div>
              <div class="col-6 col-md-3">
                <label class="form-label">Gradient End</label>
                <select class="form-select" [formControl]="form.controls['gradientEndToken']">
                  @for (t of paletteTokens; track t) {
                    <option [value]="t.key">{{t.label}}</option>
                  }
                </select>
                <div class="form-text d-flex align-items-center gap-2 mt-1">
                  <span class="d-inline-block rounded" style="width:18px;height:18px;" [style.background]="'var(' + form.controls['gradientEndToken'].value + ')'"> </span>
                </div>
              </div>
            </div>
  
            <div class="d-flex flex-wrap gap-2 mt-3">
              <button type="button" class="btn btn-primary" (click)="apply()" [disabled]="form.invalid">Apply (Preview + This Tab)</button>
              <button type="button" class="btn btn-success" (click)="save()" [disabled]="form.invalid">Save (LocalStorage)</button>
              <button type="button" class="btn btn-outline-secondary" (click)="reset()">Reset</button>
            </div>
  
            <p class="text-muted small mt-2 mb-0">Note: Saved themes persist in this browser. Hooking up server persistence can be added later.</p>
          </div>
        </div>
      </div>
  
      <!-- Live Preview -->
      <div class="col-12">
        <div class="card shadow-sm border-0 card-rounded" [wizardTheme]="form.controls['theme'].value">
          <div class="card-header gradient-header text-white py-4 border-0">
            <h5 class="mb-0 fw-semibold">Preview Header</h5>
            <p class="mb-0 opacity-75">Gradient + text contrast</p>
          </div>
          <div class="card-body">
            <p class="text-muted">Buttons, text, and surfaces preview below.</p>
            <div class="d-flex flex-wrap gap-2">
              <button type="button" class="btn btn-primary">Primary</button>
              <button type="button" class="btn btn-outline-primary">Outline</button>
              <button type="button" class="btn btn-secondary">Secondary</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
  `
})
export class ThemeEditorComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  jobPath = signal<string>('');

  // Default palette token list (subset focused on accents). Labels are friendly names.
  paletteTokens: Array<{ key: string; label: string }> = [
    { key: '--indigo-400', label: 'Indigo 400' },
    { key: '--indigo-500', label: 'Indigo 500' },
    { key: '--indigo-600', label: 'Indigo 600' },
    { key: '--violet-500', label: 'Violet 500' },
    { key: '--violet-600', label: 'Violet 600' },
    { key: '--cyan-500', label: 'Cyan 500' },
    { key: '--cyan-600', label: 'Cyan 600' },
    { key: '--emerald-500', label: 'Emerald 500' },
    { key: '--emerald-600', label: 'Emerald 600' },
    { key: '--emerald-700', label: 'Emerald 700' },
  ];

  form = this.fb.nonNullable.group({
    theme: this.fb.nonNullable.control<ThemeKey>('player', { validators: [Validators.required] }),
    primaryToken: this.fb.nonNullable.control<string>('--indigo-500', { validators: [Validators.required] }),
    gradientStartToken: this.fb.nonNullable.control<string>('--indigo-500', { validators: [Validators.required] }),
    gradientEndToken: this.fb.nonNullable.control<string>('--violet-600', { validators: [Validators.required] }),
  });

  ngOnInit(): void {
    // Find :jobPath from any ancestor
    let cur: ActivatedRoute | null = this.route;
    while (cur && !cur.snapshot.paramMap.get('jobPath')) {
      cur = cur.parent;
    }
    this.jobPath.set(cur?.snapshot.paramMap.get('jobPath') ?? '');

    // Load saved values for default theme (player)
    this.loadSavedForTheme(this.form.controls.theme.value);

    // When theme changes, load saved if present
    effect(() => {
      const t = this.form.controls.theme.value;
      this.loadSavedForTheme(t);
    });
  }

  private storageKey(theme: string) {
    return `tsic:theme:${this.jobPath()}:${theme}`;
  }

  private loadSavedForTheme(theme: ThemeKey) {
    const raw = localStorage.getItem(this.storageKey(theme));
    if (!raw) {
      // No saved state; apply sensible defaults per theme from our mapping
      const d = this.defaultTokensFor(theme);
      this.form.patchValue({
        primaryToken: d.primaryToken,
        gradientStartToken: d.gradientStartToken,
        gradientEndToken: d.gradientEndToken
      }, { emitEvent: false });
      this.apply();
      return;
    }
    try {
      const obj = JSON.parse(raw);
      if (obj?.primaryToken && obj?.gradientStartToken && obj?.gradientEndToken) {
        this.form.patchValue({
          primaryToken: obj.primaryToken,
          gradientStartToken: obj.gradientStartToken,
          gradientEndToken: obj.gradientEndToken
        }, { emitEvent: false });
      } else if (obj?.primary && obj?.gradientStart && obj?.gradientEnd) {
        // Back-compat (hex values previously saved)
        this.applyLegacy(theme, obj.primary, obj.gradientStart, obj.gradientEnd);
        const d = this.defaultTokensFor(theme);
        this.form.patchValue(d, { emitEvent: false });
      }
      this.apply();
    } catch { /* ignore */ }
  }

  private hexToRgb(hex: string): string | null {
    const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
    if (!m) return null;
    const r = Number.parseInt(m[1], 16);
    const g = Number.parseInt(m[2], 16);
    const b = Number.parseInt(m[3], 16);
    return `${r}, ${g}, ${b}`;
  }

  private defaultTokensFor(theme: ThemeKey) {
    switch (theme) {
      case 'landing':
        return { primaryToken: '--violet-600', gradientStartToken: '--violet-600', gradientEndToken: '--indigo-600' };
      case 'login':
        return { primaryToken: '--cyan-600', gradientStartToken: '--cyan-600', gradientEndToken: '--violet-600' };
      case 'role-select':
        return { primaryToken: '--indigo-500', gradientStartToken: '--indigo-500', gradientEndToken: '--cyan-600' };
      case 'family':
        return { primaryToken: '--emerald-600', gradientStartToken: '--emerald-500', gradientEndToken: '--emerald-700' };
      case 'player':
      default:
        return { primaryToken: '--indigo-500', gradientStartToken: '--indigo-500', gradientEndToken: '--violet-600' };
    }
  }

  private ensureStyleElement(): HTMLStyleElement {
    const id = `tsic-theme-${this.jobPath()}`;
    let style = document.getElementById(id) as HTMLStyleElement | null;
    if (!style) {
      style = document.createElement('style');
      style.id = id;
      document.head.appendChild(style);
    }
    return style;
  }

  private buildCssFromTokens(theme: ThemeKey, primaryToken: string, startToken: string, endToken: string): string {
    // Use semantic variable mapping so dark mode inherits correctly
    return `\n.wizard-theme-${theme} {\n  --color-primary: var(${primaryToken});\n  --bs-primary: var(${primaryToken});\n  --gradient-start: var(${startToken});\n  --gradient-end: var(${endToken});\n  --gradient-primary-start: var(${startToken});\n  --gradient-primary-end: var(${endToken});\n}`;
  }

  private buildCssLegacy(theme: ThemeKey, primaryHex: string, startHex: string, endHex: string): string {
    const rgb = this.hexToRgb(primaryHex) ?? '106, 90, 205';
    return `\n.wizard-theme-${theme} {\n  --gradient-primary-start: ${startHex};\n  --gradient-primary-end: ${endHex};\n  --bs-primary: ${primaryHex};\n  --bs-primary-rgb: ${rgb};\n}`;
  }

  apply(): void {
    const t = this.form.controls.theme.value;
    const primaryToken = this.form.controls.primaryToken.value;
    const startToken = this.form.controls.gradientStartToken.value;
    const endToken = this.form.controls.gradientEndToken.value;

    // Merge saved CSS for all themes so switching pages keeps overrides
    const savedLanding = localStorage.getItem(this.storageKey('landing'));
    const savedLogin = localStorage.getItem(this.storageKey('login'));
    const savedRole = localStorage.getItem(this.storageKey('role-select'));
    const savedPlayer = localStorage.getItem(this.storageKey('player'));
    const savedFamily = localStorage.getItem(this.storageKey('family'));
    const cssParts: string[] = [];
    const tryPush = (theme: ThemeKey, raw: string | null) => {
      if (!raw) return;
      try {
        const obj = JSON.parse(raw);
        if (obj.primaryToken && obj.gradientStartToken && obj.gradientEndToken) {
          cssParts.push(this.buildCssFromTokens(theme, obj.primaryToken, obj.gradientStartToken, obj.gradientEndToken));
        } else if (obj.primary && obj.gradientStart && obj.gradientEnd) {
          cssParts.push(this.buildCssLegacy(theme, obj.primary, obj.gradientStart, obj.gradientEnd));
        }
      } catch { /* noop */ }
    };

    tryPush('landing', savedLanding);
    tryPush('login', savedLogin);
    tryPush('role-select', savedRole);
    tryPush('player', savedPlayer);
    tryPush('family', savedFamily);

    // Always include current in-memory form values for immediate preview
    cssParts.push(this.buildCssFromTokens(t, primaryToken, startToken, endToken));

    const style = this.ensureStyleElement();
    style.textContent = cssParts.join('\n');
  }

  save(): void {
    const t = this.form.controls.theme.value;
    const data = {
      primaryToken: this.form.controls.primaryToken.value,
      gradientStartToken: this.form.controls.gradientStartToken.value,
      gradientEndToken: this.form.controls.gradientEndToken.value
    };
    localStorage.setItem(this.storageKey(t), JSON.stringify(data));
    this.apply();
  }

  reset(): void {
    const t = this.form.controls.theme.value;
    localStorage.removeItem(this.storageKey(t));
    // Rebuild style tag from any remaining saved themes except the one just cleared
    const style = this.ensureStyleElement();
    const themes: ThemeKey[] = ['landing', 'login', 'role-select', 'player', 'family'];
    const cssParts: string[] = [];
    for (const th of themes) {
      if (th === t) continue;
      const raw = localStorage.getItem(this.storageKey(th));
      if (!raw) continue;
      try {
        const obj = JSON.parse(raw);
        if (obj.primaryToken && obj.gradientStartToken && obj.gradientEndToken) {
          cssParts.push(this.buildCssFromTokens(th, obj.primaryToken, obj.gradientStartToken, obj.gradientEndToken));
        } else if (obj.primary && obj.gradientStart && obj.gradientEnd) {
          cssParts.push(this.buildCssLegacy(th, obj.primary, obj.gradientStart, obj.gradientEnd));
        }
      } catch { /* ignore */ }
    }
    style.textContent = cssParts.join('\n');
  }

  private applyLegacy(theme: ThemeKey, primaryHex: string, startHex: string, endHex: string) {
    const style = this.ensureStyleElement();
    // Merge with existing
    const existing = style.textContent ?? '';
    const css = this.buildCssLegacy(theme, primaryHex, startHex, endHex);
    style.textContent = [existing, css].filter(Boolean).join('\n');
  }
}
