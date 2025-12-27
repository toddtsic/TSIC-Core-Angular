# Bootstrap Icons Configuration

## Problem

Bootstrap Icons were not displaying in the application despite the package being installed. Icons were used throughout the codebase (accordion headers, tooltips, alerts, etc.) but none were visible.

## Root Cause

When Bootstrap Icons CSS was included via `angular.json` styles array, the relative font paths in the CSS file (`./fonts/bootstrap-icons.woff2`) were not being resolved correctly by Angular's build system. The CSS loaded but the browser couldn't find the font files.

Additionally, Syncfusion's Bootstrap theme was loading **after** Bootstrap Icons in the styles array, which can override the icon font-family declarations.

## Solution

Use SCSS `@import` instead of angular.json styles array, combined with proper asset configuration.

### 1. Import in styles.scss

**File:** `src/styles.scss` (line 3)

```scss
/* Bootstrap Icons */
@import '../node_modules/bootstrap-icons/font/bootstrap-icons.css';
```

**Why this works:** Angular 21's new `application` builder requires relative paths from the importing file. The path resolves correctly and Angular's build system properly bundles the fonts.

**Note:** Prior to Angular 21, `@import 'bootstrap-icons/font/bootstrap-icons.css'` worked, but the new esbuild-based builder requires the explicit relative path.

### 2. Asset Configuration

**File:** `angular.json` (in the `assets` array)

```json
{
  "glob": "**/*",
  "input": "node_modules/bootstrap-icons/font",
  "output": "/bootstrap-icons"
}
```

**Why this is needed:** Copies the entire bootstrap-icons font directory (including the fonts subdirectory) to the output, preserving the proper directory structure.

### 3. Styles Loading Order

**File:** `angular.json` (in the `styles` array)

```json
"styles": [
  "node_modules/bootstrap/dist/css/bootstrap.min.css",
  "node_modules/@syncfusion/ej2-bootstrap5-theme/styles/bootstrap5-lite.css",
  "src/styles/_tokens.scss",
  "src/styles/_utilities.scss",
  "src/styles.scss"
]
```

**Important:** Bootstrap Icons are loaded via the `@import` in `styles.scss`, which comes **after** Syncfusion. This ensures Syncfusion doesn't override the icon font declarations.

## Key Takeaways

1. **SCSS imports > angular.json styles** for CSS files with relative font paths
2. **Load order matters** - Syncfusion theme must load before Bootstrap Icons
3. **Copy entire font directory** to preserve proper structure (not just the .woff/.woff2 files)
4. After making changes to `angular.json`, **restart the dev server** for changes to take effect

## Usage

Bootstrap Icons can now be used anywhere with the standard syntax:

```html
<i class="bi bi-info-circle"></i>
<i class="bi bi-exclamation-triangle-fill"></i>
<i class="bi bi-list-ul"></i>
```

Icons will properly display in light and dark modes with appropriate styling.

## Date Implemented

December 19, 2025

## Updated for Angular 21

December 27, 2025 - Changed import path from `'bootstrap-icons/font/...'` to `'../node_modules/bootstrap-icons/font/...'` to accommodate Angular 21's new `application` builder which uses esbuild and requires explicit relative paths for node_modules imports.
