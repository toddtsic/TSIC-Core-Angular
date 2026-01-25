# Bulletin Internal Link Strategy

## Overview
Bulletins returned from the API contain HTML with legacy ASP.NET MVC URLs (e.g., `StartARegistration?bPlayer=true`). We translate these to Angular routes, then prevent full-page reloads by routing through Angular. Security is maintained through sanitization and job-path validation.

## Problem Statement
- API returns bulletin HTML with legacy ASP.NET MVC URLs: `<a href="StartARegistration?index=bPlayer=True">`
- These need translation to Angular routes: `/{jobPath}/register-player`
- Default browser behavior: clicking loads entire page (full reload, layout destruction)
- Security risk: API could inject links to other jobs or arbitrary URLs

## Solution Architecture

### Layer 1: URL Translation (Pipe)
**Pipe**: `TranslateLegacyUrlsPipe`

**Responsibility**: Transform legacy URLs to Angular routes before rendering

**Translations**:
- `StartARegistration?...&bPlayer=True` → `/{jobPath}/register-player`
- `StartARegistration?...&bClubRep=True` → `/{jobPath}/register-team`

**Security**: The pipe only modifies `href` attributes. The following layer (sanitizer) still applies.

### Layer 2: HTML Sanitization (Angular Built-In)
When `[innerHTML]` binding renders the pipe output:
- Angular's `DomSanitizer` automatically removes malicious scripts, event handlers, and dangerous attributes
- Valid HTML structure (including translated `<a>` tags) is preserved
- **No custom bypass needed** - standard Angular security applies
- Prevents XSS attacks even if the API returns malicious content

### Layer 3: Click Interception with Job Validation
**Directive**: `InternalLinkDirective`

**Responsibility**: Intercept clicks on translated links and validate before routing

**Security Gate**:
```typescript
// Only route if link is for CURRENT job
if (!href.startsWith(`/${currentJobPath}/`)) {
    return; // Ignore - falls back to default browser behavior
}
```

**What gets routed**:
- ✅ `/{jobPath}/register-player` - same job, routed via Angular Router
- ✅ `/{jobPath}/home` - same job, routed via Angular Router

**What is ignored** (default browser behavior):
- ❌ `/{otherJob}/register-player` - different job (blocked)
- ❌ `/admin` - outside job context (blocked)
- ❌ `http://example.com` - external (browser handles)
- ❌ `#section` - anchor (browser scrolls)
- ❌ `./document.pdf` - relative path (browser downloads)
- ❌ `mailto:user@example.com` - special scheme (OS handles)

### Layer 3: Router Configuration
**Config**: `onSameUrlNavigation: 'ignore'` in `app.config.ts`

**Purpose**: Prevents layout component recreation when navigating to same route
- Clicking "home" while on home doesn't recreate layout/banner

## Data Flow

```
API Response (contains StartARegistration?bPlayer=True URLs)
    ↓
TranslateLegacyUrlsPipe (transforms to /{jobPath}/register-player)
    ↓
[innerHTML] with DomSanitizer (blocks malicious content)
    ↓
User clicks link in bulletin
    ↓
InternalLinkDirective receives click event
    ↓
Validates: href.startsWith(`/${currentJobPath}/`) ?
    ├─ YES → router.navigateByUrl() (no full reload)
    └─ NO → ignore, browser default behavior
```

## Security Properties

| Threat | Mitigation |
|--------|-----------|
| Legacy URL format incompatibility | Pipe translates `StartARegistration` patterns to Angular routes |
| Malicious scripts in bulletin HTML | Angular's built-in DomSanitizer blocks `<script>`, `onclick`, etc. |
| Cross-job navigation from API | Directive validates `jobPath` match before routing |
| External link injection | Links to `http://`, `https://` bypass directive, handled by browser |
| Session fixation via anchor | Anchor links (`#`) bypass directive, handled by browser |
| File download manipulation | Relative paths (`./`, `../`) bypass directive, handled by browser |

## Implementation Details

### Directive Input
```typescript
jobPath = input<string>(''); // Current job path, validated against link
```

### Template Usage
```html
<div appInternalLink [jobPath]="jobPath()">
    <div [innerHTML]="bulletin.text | translateLegacyUrls:jobPath()"></div>
</div>
```

Note: `TranslateLegacyUrlsPipe` translates API responses and is actively used to convert legacy `StartARegistration` URLs to Angular routes.

## Future Considerations

1. **If translation patterns change**: Modify the pipe's `translateUrl()` method to handle new legacy URL formats
2. **If new content types added**: Same directive/sanitizer pattern applies to any `[innerHTML]` content
3. **If cross-job navigation needed**: Add explicit allowlist (e.g., admin console linking to other jobs)

## Testing Checklist

- [ ] Clicking bulletin link routes to correct page without full reload
- [ ] Clicking bulletin link in same job navigates without recreating layout
- [ ] Layout/banner persists on navigation within same job
- [ ] External links in bulletins open in new tab
- [ ] PDF/download links work normally
- [ ] API cannot inject cross-job links (test with malicious URL in bulletin.text)
