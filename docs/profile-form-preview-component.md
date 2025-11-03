# Profile Form Preview Component

**Created:** November 1, 2025  
**Purpose:** Dynamically render form preview from ProfileMetadata JSON for visual validation of field ordering and configuration

## Overview

The Profile Form Preview Component provides an interactive visual representation of player profile forms based on metadata parsed from POCO classes. This allows administrators to:

- **Validate Field Ordering**: See numbered fields in their display order
- **Review Input Types**: Verify correct input controls (TEXT, SELECT, DATE, etc.)
- **Check Dropdown Options**: Preview actual dropdown data for SELECT fields
- **Assess Validation Rules**: View validation hints and requirements
- **Identify Admin/Computed Fields**: See special field badges

## Location

```
TSIC-Core-Angular/src/frontend/tsic-app/src/app/
├── shared/components/profile-form-preview/
│   ├── profile-form-preview.component.ts
│   ├── profile-form-preview.component.html
│   └── profile-form-preview.component.scss
└── core/services/
    └── form-field-data.service.ts
```

## Architecture

### Components

#### ProfileFormPreviewComponent

**Responsibilities:**
- Accept ProfileMetadata as input
- Build reactive Angular FormGroup from field definitions
- Render form controls based on input types
- Display field metadata (order, validation, data source)
- Support read-only and editable modes

**Key Features:**
- **Signals-based reactive state**: Uses Angular 18 signals for efficient change detection
- **Computed sorted fields**: Auto-sorts fields by order property
- **Dynamic form generation**: Creates FormGroup from metadata at runtime
- **Icon mapping**: Bootstrap Icons for each input type
- **Responsive grid**: 2-column layout (mobile: 1 column)

### Services

#### FormFieldDataService

**Purpose:** Provide sample/mock data for SELECT dropdown options

**Data Sources Available:**
- `genders` - Male, Female, Other
- `positions` - Lacrosse positions (Attack, Midfield, Defense, Goalie, LSM, FOGO)
- `gradYears` - Auto-generated (current year + 10 years)
- `schoolGrades` - 6th through 12th grade
- `skillLevels` - Beginner, Intermediate, Advanced, Elite
- `states` - All 50 US states with abbreviations
- `teams` - Sample team data (will be replaced with actual API calls)
- `agegroups` - U10, U12, U14, U16, U18
- `jerseySizes`, `shortsSizes`, `shirtSizes`, etc. - Apparel sizing
- `handedness` - Right, Left, Both

**Future:** Replace with actual API calls to job-specific data endpoints

## Usage

### Basic Integration

```typescript
import { ProfileFormPreviewComponent } from './shared/components/profile-form-preview/profile-form-preview.component';

@Component({
    imports: [ProfileFormPreviewComponent]
})
export class MyComponent {
    metadata = signal<ProfileMetadata | null>(null);
}
```

```html
<app-profile-form-preview 
    [metadata]="metadata()" 
    [showFieldNumbers]="true"
    [showValidationHints]="true"
    [readonly]="true">
</app-profile-form-preview>
```

### Component Inputs

| Input | Type | Default | Description |
|-------|------|---------|-------------|
| `metadata` | `ProfileMetadata \| null` | `null` | Profile metadata to render |
| `showFieldNumbers` | `boolean` | `true` | Display order numbers as badges |
| `showValidationHints` | `boolean` | `true` | Show validation rule summaries |
| `readonly` | `boolean` | `true` | Disable form inputs for preview mode |

### Profile Migration Integration

The component is integrated into the Profile Migration modal with a toggle:

```html
<div class="card-header d-flex justify-content-between align-items-center">
    <h6 class="mb-0"><i class="bi bi-ui-checks"></i> Form Preview</h6>
    <button class="btn btn-sm btn-outline-secondary" (click)="toggleJsonView()">
        @if (showJsonView()) {
            <i class="bi bi-ui-checks"></i> Show Form
        } @else {
            <i class="bi bi-code-square"></i> Show JSON
        }
    </button>
</div>

<div class="card-body">
    @if (showJsonView()) {
        <!-- JSON View -->
        <pre>{{ previewResult()!.generatedMetadata | json }}</pre>
    } @else {
        <!-- Form Preview -->
        <app-profile-form-preview [metadata]="previewResult()?.generatedMetadata ?? null">
        </app-profile-form-preview>
    }
</div>
```

## Input Type Rendering

### TEXT, EMAIL, TEL
```html
<input type="text|email|tel" class="form-control" [formControlName]="field.name">
```

### NUMBER
```html
<input type="number" class="form-control" 
    [min]="field.validation?.min ?? null" 
    [max]="field.validation?.max ?? null">
```

### DATE
```html
<input type="date" class="form-control" [formControlName]="field.name">
```

### SELECT
```html
<select class="form-select" [formControlName]="field.name">
    <option value="">-- Select {{ field.displayName }} --</option>
    @for (option of getDropdownOptions(field); track option.value) {
        <option [value]="option.value">{{ option.label }}</option>
    }
</select>
<small class="text-muted">Data Source: <code>{{ field.dataSource }}</code></small>
```

### CHECKBOX
```html
<div class="form-check">
    <input type="checkbox" class="form-check-input" [formControlName]="field.name">
    <label class="form-check-label">{{ field.displayName }}</label>
</div>
```

### FILE
```html
<input type="file" class="form-control" [disabled]="readonly">
<small class="text-muted">File upload field (preview only)</small>
```

### HIDDEN
Hidden fields are rendered for visibility review with a distinct visual style and an actual hidden input element:

- Cyan left accent bar with subtle diagonal stripes
- "Hidden" badge
- A `<input type="hidden">` control bound to the field (not user-editable)

This makes hidden/system fields easy to spot in the preview without exposing them as editable inputs.

## Validation Hints

The component generates user-friendly validation hints from field metadata:

```typescript
getValidationHint(field: ProfileMetadataField): string {
    const hints: string[] = [];
    
    if (field.validation?.required) hints.push('Required');
    if (field.validation?.minLength) hints.push(`Min ${field.validation.minLength} chars`);
    if (field.validation?.maxLength) hints.push(`Max ${field.validation.maxLength} chars`);
    if (field.validation?.min) hints.push(`Min ${field.validation.min}`);
    if (field.validation?.max) hints.push(`Max ${field.validation.max}`);
    if (field.validation?.pattern) hints.push('Pattern validation');
    if (field.validation?.email) hints.push('Valid email required');
    if (field.validation?.remote) hints.push('Remote validation');
    
    return hints.join(' • ');
}
```

**Example Output:**
```
Required • Min 2 chars • Max 50 chars
```

## Field Metadata Display

Each field shows:

1. **Order Badge**: Sequential number for field ordering
2. **Icon**: Bootstrap Icon matching input type
3. **Display Name**: User-friendly label
4. **Required Indicator**: Red asterisk for required fields
5. **Special Badges**:
   - **Admin Only**: Yellow badge for admin-restricted fields
   - **Computed**: Blue badge for calculated fields
6. **Validation Hints**: Summary of validation rules
7. **Custom Messages**: Field-specific validation messages
8. **Database Info**: `dbColumn` and JSON `name` properties

## Styling

### Visual Styling

```scss
.form-field-preview {
    background: #f8f9fa;
    padding: 1rem;
    border-radius: 0.375rem;
    border: 1px solid #dee2e6;
    transition: all 0.2s ease;

    &:hover {
        border-color: #0d6efd;
        box-shadow: 0 0 0 0.2rem rgba(13, 110, 253, 0.1);
    }

    /* Admin-only fields: yellow accent and subtle stripes */
    &.admin-only {
        position: relative;
        border-left: 6px solid #ffc107; /* warning */
        background-image: repeating-linear-gradient(
            45deg,
            rgba(255, 193, 7, 0.12),
            rgba(255, 193, 7, 0.12) 10px,
            rgba(255, 193, 7, 0.06) 10px,
            rgba(255, 193, 7, 0.06) 20px
        );
    }

    /* Hidden fields: cyan accent and subtle stripes */
    &.hidden-field {
        position: relative;
        border-left: 6px solid #0dcaf0; /* info */
        background-image: repeating-linear-gradient(
            45deg,
            rgba(13, 202, 240, 0.12),
            rgba(13, 202, 240, 0.12) 10px,
            rgba(13, 202, 240, 0.06) 10px,
            rgba(13, 202, 240, 0.06) 20px
        );
    }
}
```

This styling clearly separates Public, Admin-only, and Hidden fields at a glance. Fields also highlight on hover to improve visual feedback during review.

### Responsive Grid

```html
<div class="row">
    <div class="col-md-6 mb-3">
        <!-- Field -->
    </div>
</div>
```

- **Desktop**: 2-column grid
- **Mobile**: Single column (stacks vertically)

## TypeScript Type Safety

### Nullable Attributes

Angular 18 with TypeScript 5.5.4 requires strict null handling for HTML attribute bindings:

```html
<!-- ✅ CORRECT -->
[min]="field.validation?.min ?? null"

<!-- ❌ INCORRECT -->
[min]="field.validation?.min ?? undefined"
```

HTML attributes like `min`, `max` accept `string | number | null` but NOT `undefined`.

### Optional Chaining

```html
<!-- Safe access to nested optional properties -->
{{ field.validation?.message }}
```

Prevents "Object is possibly 'undefined'" TypeScript errors.

## Future Enhancements

### Phase 1: API Integration
- Replace `FormFieldDataService` mock data with actual API calls
- Load teams, positions, age groups from job context
- Support dynamic data sources per job

### Phase 2: Interactive Editing
- Add drag-and-drop field reordering
- Enable inline validation rule editing
- Support adding/removing fields
- Real-time metadata JSON updates

### Phase 3: Profile Editor Integration
- Use component in full Profile Editor (not just migration preview)
- Support player data binding for actual registration forms
- Add form submission and validation

### Phase 4: Advanced Features
- Conditional field visibility rules
- Multi-page form layouts
- Field dependencies (e.g., "if gender=M, show...")
- Custom field types (address, phone with formatting)

## Troubleshooting

### Form Not Displaying

**Symptom:** Preview shows JSON instead of form  
**Cause:** TypeScript compilation errors preventing component from loading  
**Solution:** Check Angular CLI output for compilation errors, fix type issues, restart dev server

### Dropdowns Empty

**Symptom:** SELECT fields show "No options available"  
**Cause:** Missing or mismatched `dataSource` mapping  
**Solution:** Add data source to `FormFieldDataService.dataSourceMappings`

### Validation Hints Not Showing

**Symptom:** `showValidationHints=true` but no hints appear  
**Cause:** Field has no validation metadata  
**Solution:** Ensure POCO class has validation attributes (DataAnnotations or FluentValidation)

## Related Documentation

- [Profile Migration Architecture](./profile-migration-angular-implementation.md)
- [Angular Coding Standards](../specs/ANGULAR-CODING-STANDARDS.md)
- [Player Registration Architecture](./player-registration-architecture.md)
- [Profile Metadata Editor Design](./profile-metadata-editor-design.md)

## Session Notes

**November 1, 2025:**
- Created ProfileFormPreviewComponent with full input type support
- Created FormFieldDataService with comprehensive dropdown data
- Integrated into Profile Migration modal with toggle
- Fixed TypeScript compilation errors (nullable attribute handling)
- Component ready for use, requires Angular dev server restart

**Implementation Time:** ~2 hours (component + service + integration + debugging)  
**Status:** ✅ Complete, pending server restart for compilation
