import { Component, OnInit, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { WizardThemeDirective } from '../../shared/directives/wizard-theme.directive';

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
    imports: [CommonModule, ReactiveFormsModule, WizardThemeDirective],
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
              <div class="col-6 col-md-3">
                <label class="form-label">Primary</label>
                <input type="color" class="form-control form-control-color w-100" [formControl]="form.controls['primary']" />
              </div>
              <div class="col-6 col-md-3">
                <label class="form-label">Gradient Start</label>
                <input type="color" class="form-control form-control-color w-100" [formControl]="form.controls['gradientStart']" />
              </div>
              <div class="col-6 col-md-3">
                <label class="form-label">Gradient End</label>
                <input type="color" class="form-control form-control-color w-100" [formControl]="form.controls['gradientEnd']" />
              </div>
            </div>

            <div class="d-flex flex-wrap gap-2 mt-3">
              <button type="button" class="btn btn-primary" (click)="apply()" [disabled]="form.invalid">Apply (Preview + This Tab)</button>
              <button type="button" class="btn btn-success" (click)="save()" [disabled]="form.invalid">Save (LocalStorage)</button>
              <button type="button" class="btn btn-outline-secondary" (click)="reset()">Reset</button>
            </div>

            <p class="text-secondary small mt-2 mb-0">Note: Saved themes persist in this browser. Hooking up server persistence can be added later.</p>
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
            <p class="text-secondary">Buttons, text, and surfaces preview below.</p>
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

    form = this.fb.nonNullable.group({
        theme: this.fb.nonNullable.control<ThemeKey>('player', { validators: [Validators.required] }),
        primary: this.fb.nonNullable.control<string>('#6a5acd', { validators: [Validators.required] }),
        gradientStart: this.fb.nonNullable.control<string>('#6a5acd', { validators: [Validators.required] }),
        gradientEnd: this.fb.nonNullable.control<string>('#7b68ee', { validators: [Validators.required] }),
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
        if (!raw) return;
        try {
            const obj = JSON.parse(raw) as { primary: string; gradientStart: string; gradientEnd: string };
            this.form.patchValue({
                primary: obj.primary,
                gradientStart: obj.gradientStart,
                gradientEnd: obj.gradientEnd
            }, { emitEvent: false });
            // Also apply live so preview reflects saved
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

    private buildCss(theme: ThemeKey, primary: string, start: string, end: string): string {
        const rgb = this.hexToRgb(primary) ?? '106, 90, 205';
        return `\n.wizard-theme-${theme} {\n  --gradient-primary-start: ${start};\n  --gradient-primary-end: ${end};\n  --bs-primary: ${primary};\n  --bs-primary-rgb: ${rgb};\n}`;
    }

    apply(): void {
        const t = this.form.controls.theme.value;
        const primary = this.form.controls.primary.value;
        const start = this.form.controls.gradientStart.value;
        const end = this.form.controls.gradientEnd.value;

        // Merge saved CSS for all themes so switching pages keeps overrides
        const savedLanding = localStorage.getItem(this.storageKey('landing'));
        const savedLogin = localStorage.getItem(this.storageKey('login'));
        const savedRole = localStorage.getItem(this.storageKey('role-select'));
        const savedPlayer = localStorage.getItem(this.storageKey('player'));
        const savedFamily = localStorage.getItem(this.storageKey('family'));
        const cssParts: string[] = [];
        if (savedLanding) {
            try { const sln = JSON.parse(savedLanding); cssParts.push(this.buildCss('landing', sln.primary, sln.gradientStart, sln.gradientEnd)); } catch { /* noop */ }
        }
        if (savedLogin) {
            try { const sl = JSON.parse(savedLogin); cssParts.push(this.buildCss('login', sl.primary, sl.gradientStart, sl.gradientEnd)); } catch { /* noop */ }
        }
        if (savedRole) {
            try { const sr = JSON.parse(savedRole); cssParts.push(this.buildCss('role-select', sr.primary, sr.gradientStart, sr.gradientEnd)); } catch { /* noop */ }
        }
        if (savedPlayer) {
            try { const sp = JSON.parse(savedPlayer); cssParts.push(this.buildCss('player', sp.primary, sp.gradientStart, sp.gradientEnd)); } catch { /* noop */ }
        }
        if (savedFamily) {
            try { const sf = JSON.parse(savedFamily); cssParts.push(this.buildCss('family', sf.primary, sf.gradientStart, sf.gradientEnd)); } catch { /* noop */ }
        }
        // Always include current in-memory form values for immediate preview
        cssParts.push(this.buildCss(t, primary, start, end));

        const style = this.ensureStyleElement();
        style.textContent = cssParts.join('\n');
    }

    save(): void {
        const t = this.form.controls.theme.value;
        const data = {
            primary: this.form.controls.primary.value,
            gradientStart: this.form.controls.gradientStart.value,
            gradientEnd: this.form.controls.gradientEnd.value
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
                cssParts.push(this.buildCss(th, obj.primary, obj.gradientStart, obj.gradientEnd));
            } catch { /* ignore */ }
        }
        style.textContent = cssParts.join('\n');
    }
}
