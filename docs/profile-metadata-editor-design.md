# Profile Metadata Editor - Design Document

**Date**: October 31, 2025  
**Status**: Design Phase  
**Related**: `player-registration-architecture.md`

---

## Executive Summary

The Profile Metadata Editor is an Angular-based admin tool that allows superusers to create and edit `PlayerProfileMetadataJson` for job registrations. This editor eliminates the need for manual JSON editing and provides a visual interface for configuring registration form fields.

### Key Features

1. **Hybrid Import System**: Manual C# paste (Phase 1) → GitHub API automation (Phase 2)
2. **Visual Form Builder**: Drag-and-drop field ordering, property panel editing
3. **Live Preview**: Real-time form rendering as fields are configured
4. **JSON Export**: Generate and save `PlayerProfileMetadataJson` to database
5. **Validation**: Ensure metadata structure is correct before saving

---

## User Journey

### Superuser Workflow

1. **Navigate to Editor**: Click "Profile Metadata Editor" card on job home page
2. **Load Existing or Start New**:
   - If job has `PlayerProfileMetadataJson` → Load and edit
   - If empty → Start from scratch or import from C# class
3. **Import Options** (Phase 1):
   - Click "Import from C# Class" button
   - Paste C# class code into textarea
   - Backend parses using Roslyn
   - Editor populates with extracted metadata
4. **Edit Fields**:
   - Add/remove fields
   - Configure properties (name, type, validation, order)
   - Drag to reorder
   - Toggle admin-only visibility
5. **Preview Form**: See live rendering of registration form
6. **Save**: Update `Job.PlayerProfileMetadataJson` in database
7. **Test**: Navigate to registration flow and verify form

---

## Architecture

### Component Structure

```
profile-metadata-editor/
├── profile-metadata-editor.component.ts       # Main container
├── profile-metadata-editor.component.html     
├── profile-metadata-editor.component.scss     
├── components/
│   ├── field-list/                           # Left panel: field list
│   │   ├── field-list.component.ts
│   │   ├── field-list.component.html
│   │   └── field-list.component.scss
│   ├── field-editor/                         # Right panel: property editor
│   │   ├── field-editor.component.ts
│   │   ├── field-editor.component.html
│   │   └── field-editor.component.scss
│   ├── form-preview/                         # Bottom panel: live preview
│   │   ├── form-preview.component.ts
│   │   ├── form-preview.component.html
│   │   └── form-preview.component.scss
│   └── import-dialog/                        # Import C# class dialog
│       ├── import-dialog.component.ts
│       ├── import-dialog.component.html
│       └── import-dialog.component.scss
├── models/
│   └── profile-metadata.models.ts            # TypeScript interfaces
└── services/
    └── profile-metadata.service.ts           # API integration
```

---

## Data Models

### TypeScript Interfaces

```typescript
// profile-metadata.models.ts

export interface ProfileMetadata {
  profileName: string;              // PP47, CAC12, etc.
  registrationType: 'PP' | 'CAC';   // PlayerProfile or CampsAndClinics
  teamConstraint: string;           // BYGRADYEAR, BYAGEGROUP, etc.
  allowPayInFull: boolean;
  fields: FieldDefinition[];
}

export interface FieldDefinition {
  id: string;                       // Unique ID for drag-drop
  name: string;                     // Property name (camelCase)
  dbColumn: string;                 // Database column name
  displayName: string;              // User-facing label
  inputType: InputType;
  dataSource?: string;              // References JsonOptions key
  required: boolean;
  order: number;
  adminOnly: boolean;
  validation: ValidationRules;
  placeholder?: string;
  helpText?: string;
}

export type InputType = 
  | 'text' 
  | 'textarea' 
  | 'select' 
  | 'checkbox' 
  | 'date' 
  | 'number' 
  | 'email' 
  | 'tel'
  | 'radio';

export interface ValidationRules {
  required?: boolean;
  minLength?: number;
  maxLength?: number;
  min?: number;
  max?: number;
  pattern?: string;
  mustBeTrue?: boolean;              // For checkboxes (waivers)
  externalApi?: ExternalApiValidation;
}

export interface ExternalApiValidation {
  endpoint: string;                  // e.g., "/api/registration/validate-uslax"
  validThroughDateField?: string;    // Job field reference
}

export interface ImportCSharpRequest {
  csharpCode: string;                // Raw C# class code
  profileName?: string;              // Optional override
}

export interface ImportCSharpResponse {
  success: boolean;
  metadata: ProfileMetadata;
  errors?: string[];
  warnings?: string[];
}

export interface SaveMetadataRequest {
  jobId: string;
  metadata: ProfileMetadata;
}

export interface SaveMetadataResponse {
  success: boolean;
  message: string;
}
```

---

## UI Layout

### Three-Panel Layout

```
┌─────────────────────────────────────────────────────────────────┐
│ Profile Metadata Editor: Summer League 2025 (PP47)             │
│ [Import from C#] [Add Field] [Save] [Preview]                  │
├──────────────────┬──────────────────────────────────────────────┤
│                  │                                              │
│  Field List      │  Field Properties                            │
│  (Left Panel)    │  (Right Panel)                               │
│                  │                                              │
│  ☰ Jersey Size   │  Name: jerseySize                            │
│  ☰ USLax Number  │  Display Name: Jersey Size                   │
│  ☰ Special Req   │  Input Type: [select ▼]                      │
│  ☰ Waiver 1      │  Data Source: ListSizes_Jersey               │
│  ☰ Uniform # 🔒  │  Required: ☑                                 │
│                  │  Admin Only: ☐                               │
│  [+ Add Field]   │  Order: 10                                   │
│                  │  Validation:                                 │
│                  │    Min Length: ___                           │
│                  │    Max Length: ___                           │
│                  │  Placeholder: Select a size...               │
│                  │  Help Text: ___________________________      │
│                  │                                              │
│                  │  [Delete Field]                              │
│                  │                                              │
├──────────────────┴──────────────────────────────────────────────┤
│  Form Preview                                                   │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ Jersey Size *                                           │   │
│  │ [-- Select a size... ────────────────────────▼]         │   │
│  │                                                         │   │
│  │ US Lacrosse Number *                                    │   │
│  │ [_____________________________________________]          │   │
│  │                                                         │   │
│  │ Special Requests                                        │   │
│  │ [_____________________________________________]          │   │
│  │ [_____________________________________________]          │   │
│  │                                                         │   │
│  │ ☐ I agree with the Waiver Terms and Conditions *       │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Legend**:
- ☰ = Drag handle
- 🔒 = Admin-only field (shown with lock icon)
- * = Required field

---

## Phase 1: Manual C# Import

### Import Dialog Flow

1. **User clicks "Import from C# Class"**
2. **Dialog opens** with:
   - Large textarea for pasting C# code
   - Profile name input (optional override)
   - Import button
3. **User pastes C# class code**:
   ```csharp
   public class PP47_ViewModel : PP_ViewModel
   {
       [Display(Name = "Jersey Size")]
       [Required(ErrorMessage = "JERSEY SIZE is required")]
       public string JerseySize { get; set; }
       
       [Display(Name = "Special Requests")]
       public string SpecialRequests { get; set; }
   }
   ```
4. **Backend parses** using Roslyn:
   - Extract properties
   - Read DataAnnotations attributes
   - Infer input types
   - Generate field order
5. **Response populates editor**:
   - Fields appear in field list
   - User can refine properties
   - Missing metadata shown with warnings

### Backend API Endpoint

```csharp
// POST /api/admin/profile/import-csharp
[HttpPost("import-csharp")]
public async Task<ActionResult<ImportCSharpResponse>> ImportFromCSharp(
    [FromBody] ImportCSharpRequest request)
{
    try
    {
        var parser = new CSharpProfileParser();
        var metadata = await parser.ParseAsync(request.CsharpCode);
        
        return Ok(new ImportCSharpResponse
        {
            Success = true,
            Metadata = metadata,
            Warnings = parser.GetWarnings() // e.g., "InputType not specified for field X"
        });
    }
    catch (Exception ex)
    {
        return BadRequest(new ImportCSharpResponse
        {
            Success = false,
            Errors = new[] { ex.Message }
        });
    }
}
```

### C# Parser Logic (Backend)

```csharp
public class CSharpProfileParser
{
    private readonly List<string> _warnings = new();
    
    public async Task<ProfileMetadata> ParseAsync(string csharpCode)
    {
        // Use Roslyn to parse syntax tree
        var tree = CSharpSyntaxTree.ParseText(csharpCode);
        var root = await tree.GetRootAsync();
        
        // Find class declaration
        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();
            
        if (classDecl == null)
            throw new Exception("No class found in provided code");
        
        // Extract profile name from class name (PP47_ViewModel → PP47)
        var profileName = ExtractProfileName(classDecl.Identifier.Text);
        
        // Extract fields from properties
        var fields = new List<FieldDefinition>();
        var properties = classDecl.Members.OfType<PropertyDeclarationSyntax>();
        
        int order = 10;
        foreach (var prop in properties)
        {
            var field = ParseProperty(prop, order);
            fields.Add(field);
            order += 10;
        }
        
        return new ProfileMetadata
        {
            ProfileName = profileName,
            RegistrationType = profileName.StartsWith("PP") ? "PP" : "CAC",
            TeamConstraint = "", // User must configure
            AllowPayInFull = false, // User must configure
            Fields = fields
        };
    }
    
    private FieldDefinition ParseProperty(PropertyDeclarationSyntax prop, int order)
    {
        var propName = prop.Identifier.Text;
        var attributes = prop.AttributeLists.SelectMany(a => a.Attributes);
        
        // Extract Display attribute
        var displayAttr = attributes.FirstOrDefault(a => a.Name.ToString() == "Display");
        var displayName = ExtractDisplayName(displayAttr, propName);
        
        // Extract Required attribute
        var requiredAttr = attributes.FirstOrDefault(a => a.Name.ToString() == "Required");
        var isRequired = requiredAttr != null;
        
        // Infer input type from property type
        var inputType = InferInputType(prop.Type.ToString(), propName);
        
        // Check if type inference is uncertain
        if (inputType == "text" && !propName.Contains("Name") && !propName.Contains("Number"))
        {
            _warnings.Add($"Input type inferred as 'text' for {propName}. Please verify.");
        }
        
        return new FieldDefinition
        {
            Id = Guid.NewGuid().ToString(),
            Name = ToCamelCase(propName),
            DbColumn = propName,
            DisplayName = displayName,
            InputType = inputType,
            Required = isRequired,
            Order = order,
            AdminOnly = false,
            Validation = new ValidationRules
            {
                Required = isRequired
            }
        };
    }
    
    private string InferInputType(string typeName, string propName)
    {
        // Type-based inference
        if (typeName == "bool") return "checkbox";
        if (typeName == "int" || typeName == "decimal") return "number";
        if (typeName == "DateTime") return "date";
        
        // Name-based inference
        var lowerName = propName.ToLower();
        if (lowerName.Contains("email")) return "email";
        if (lowerName.Contains("phone") || lowerName.Contains("cell")) return "tel";
        if (lowerName.Contains("size") || lowerName.Contains("position")) return "select";
        if (lowerName.Contains("request") || lowerName.Contains("comment")) return "textarea";
        if (lowerName.Contains("waiver") || lowerName.Contains("agree")) return "checkbox";
        
        return "text"; // Default fallback
    }
    
    private string ExtractDisplayName(AttributeSyntax? attr, string propName)
    {
        if (attr == null)
            return AddSpacesToCamelCase(propName);
        
        var nameArg = attr.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.ToString() == "Name");
            
        if (nameArg != null)
        {
            var value = nameArg.Expression.ToString().Trim('"');
            return value;
        }
        
        return AddSpacesToCamelCase(propName);
    }
    
    public List<string> GetWarnings() => _warnings;
}
```

---

## Phase 2: GitHub API Integration

### Automated Import Flow

1. **User opens editor for job with `CoreRegformPlayer = "PP47|BYGRADYEAR|ALLOWPIF"`**
2. **Editor detects**:
   - `PlayerProfileMetadataJson` is null/empty
   - Profile name is "PP47"
3. **Show prompt**: "Auto-generate metadata from PP47 class in GitHub?"
4. **Backend fetches**:
   - GET `https://api.github.com/repos/toddtsic/TSIC-Unify-2025/contents/ViewModels/RegPlayersSingle_ViewModels/PP47_ViewModel.cs`
   - Decode base64 content
   - Parse using same Roslyn logic
5. **Populate editor** automatically

### Backend GitHub Integration

```csharp
public class GitHubProfileFetcher
{
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner = "toddtsic";
    private readonly string _repoName = "TSIC-Unify-2025";
    
    public async Task<string> FetchProfileClassAsync(string profileName)
    {
        // Determine path based on profile type
        var path = profileName.StartsWith("PP") 
            ? $"ViewModels/RegPlayersSingle_ViewModels/{profileName}_ViewModel.cs"
            : $"ViewModels/RegPlayersMulti_ViewModels/{profileName}_ViewModel.cs";
        
        var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/contents/{path}";
        
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "TSIC-Core-Angular");
        request.Headers.Add("Accept", "application/vnd.github.v3+json");
        
        // Add GitHub token if available (for higher rate limits)
        var token = _configuration["GitHub:Token"];
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Add("Authorization", $"token {token}");
        }
        
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadFromJsonAsync<GitHubFileResponse>();
        
        // Decode base64 content
        var bytes = Convert.FromBase64String(content.Content);
        return Encoding.UTF8.GetString(bytes);
    }
}

public class GitHubFileResponse
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string Content { get; set; }
    public string Encoding { get; set; }
}
```

### API Endpoint for GitHub Import

```csharp
// POST /api/admin/profile/import-from-github
[HttpPost("import-from-github")]
public async Task<ActionResult<ImportCSharpResponse>> ImportFromGitHub(
    [FromBody] ImportFromGitHubRequest request)
{
    try
    {
        var fetcher = new GitHubProfileFetcher(_httpClient, _configuration);
        var csharpCode = await fetcher.FetchProfileClassAsync(request.ProfileName);
        
        var parser = new CSharpProfileParser();
        var metadata = await parser.ParseAsync(csharpCode);
        
        return Ok(new ImportCSharpResponse
        {
            Success = true,
            Metadata = metadata,
            Warnings = parser.GetWarnings()
        });
    }
    catch (HttpRequestException ex)
    {
        return NotFound(new ImportCSharpResponse
        {
            Success = false,
            Errors = new[] { $"Profile class not found in GitHub: {ex.Message}" }
        });
    }
    catch (Exception ex)
    {
        return BadRequest(new ImportCSharpResponse
        {
            Success = false,
            Errors = new[] { ex.Message }
        });
    }
}

public class ImportFromGitHubRequest
{
    public string ProfileName { get; set; }
}
```

---

## Angular Service

```typescript
// profile-metadata.service.ts

import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ProfileMetadata,
  ImportCSharpRequest,
  ImportCSharpResponse,
  SaveMetadataRequest,
  SaveMetadataResponse
} from '../models/profile-metadata.models';

@Injectable({ providedIn: 'root' })
export class ProfileMetadataService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/admin/profile`;

  /**
   * Import profile metadata from C# class code (manual paste)
   */
  importFromCSharp(request: ImportCSharpRequest): Observable<ImportCSharpResponse> {
    return this.http.post<ImportCSharpResponse>(
      `${this.apiUrl}/import-csharp`,
      request
    );
  }

  /**
   * Import profile metadata from GitHub repository (automated)
   */
  importFromGitHub(profileName: string): Observable<ImportCSharpResponse> {
    return this.http.post<ImportCSharpResponse>(
      `${this.apiUrl}/import-from-github`,
      { profileName }
    );
  }

  /**
   * Save profile metadata to database (Job.PlayerProfileMetadataJson)
   */
  saveMetadata(request: SaveMetadataRequest): Observable<SaveMetadataResponse> {
    return this.http.post<SaveMetadataResponse>(
      `${this.apiUrl}/save`,
      request
    );
  }

  /**
   * Load existing profile metadata for a job
   */
  loadMetadata(jobId: string): Observable<ProfileMetadata> {
    return this.http.get<ProfileMetadata>(
      `${this.apiUrl}/load/${jobId}`
    );
  }

  /**
   * Validate metadata structure before saving
   */
  validateMetadata(metadata: ProfileMetadata): Observable<{ valid: boolean; errors: string[] }> {
    return this.http.post<{ valid: boolean; errors: string[] }>(
      `${this.apiUrl}/validate`,
      metadata
    );
  }
}
```

---

## Field Editor Features

### Field List (Left Panel)

**Features**:
- Drag-and-drop reordering (updates `order` property)
- Click to select field for editing
- Visual indicators:
  - Required fields: Red asterisk
  - Admin-only: Lock icon
  - Invalid fields: Warning icon
- Add field button
- Delete confirmation

**Implementation**:
```typescript
// field-list.component.ts
export class FieldListComponent {
  fields = input.required<FieldDefinition[]>();
  selectedField = model<FieldDefinition | null>(null);
  
  onDrop(event: CdkDragDrop<FieldDefinition[]>) {
    const fields = [...this.fields()];
    moveItemInArray(fields, event.previousIndex, event.currentIndex);
    
    // Reorder field order values
    fields.forEach((field, index) => {
      field.order = (index + 1) * 10;
    });
    
    this.fieldsChange.emit(fields);
  }
}
```

### Field Properties (Right Panel)

**Editable Properties**:
- Name (camelCase validation)
- Display Name (shown to users)
- Input Type (dropdown with icons)
- Data Source (autocomplete from Job.JsonOptions keys)
- Required toggle
- Admin Only toggle
- Order (manual numeric input)
- Validation rules (conditional based on input type)
- Placeholder text
- Help text

**Conditional Validation Fields**:
- **Text/Textarea**: minLength, maxLength, pattern
- **Number**: min, max
- **Select**: dataSource required
- **Checkbox**: mustBeTrue option
- **All**: externalApi configuration

### Form Preview (Bottom Panel)

**Features**:
- Live rendering as fields are edited
- Bootstrap styling (matches actual registration form)
- Shows validation errors
- Toggle between user view and admin view
- Mobile responsive preview

---

## Validation Rules

### Metadata Validation

Before saving, validate:
- ✅ Profile name is not empty
- ✅ At least one field defined
- ✅ All field names are unique
- ✅ All field names are valid C# property names
- ✅ All required fields have display names
- ✅ Select fields have valid dataSource
- ✅ DataSource references exist in Job.JsonOptions
- ✅ Order values are unique
- ✅ External API endpoints are valid URLs

### Field Validation

- **Name**: Must be camelCase, no spaces, valid identifier
- **Display Name**: Required, max 200 chars
- **Input Type**: Must be valid enum value
- **Data Source**: Required for select/radio, must exist in JsonOptions
- **Order**: Must be positive integer

---

## Save Flow

1. **User clicks Save**
2. **Validate metadata** (frontend + backend)
3. **Show confirmation** with summary:
   - Number of fields
   - Required fields count
   - Admin-only fields count
4. **POST to API**:
   ```typescript
   saveMetadata(jobId: string, metadata: ProfileMetadata) {
     this.loading.set(true);
     
     this.metadataService.saveMetadata({ jobId, metadata }).subscribe({
       next: (response) => {
         this.loading.set(false);
         this.showSuccess('Profile metadata saved successfully!');
         this.router.navigate(['/tsic', jobPath, 'admin']);
       },
       error: (error) => {
         this.loading.set(false);
         this.showError(error.message);
       }
     });
   }
   ```
5. **Backend updates** `Job.PlayerProfileMetadataJson`
6. **Redirect** to admin dashboard or job home

---

## Future Enhancements

### Phase 3 Features

- **Field Templates**: Pre-built fields (USLax Number, Jersey Size, Waivers)
- **Duplicate Field**: Clone existing field configuration
- **Field Groups**: Organize fields into sections
- **Conditional Logic**: Show/hide fields based on other field values
- **Multi-Language**: Display names in multiple languages
- **Version History**: Track changes to metadata over time
- **Export/Import JSON**: Download/upload metadata files
- **Bulk Edit**: Update multiple fields at once
- **Field Search**: Filter large field lists
- **Undo/Redo**: Revert recent changes

### GitHub Sync Features

- **Auto-update**: Detect changes in GitHub repo and prompt to refresh
- **Diff View**: Show differences between current and GitHub version
- **Two-way sync**: Push changes back to GitHub (create PR)
- **Branch selection**: Import from specific branch/tag

---

## Testing Strategy

### Unit Tests

**Service Tests**:
- Import C# code parsing
- GitHub API integration
- Validation logic
- Save/load operations

**Component Tests**:
- Field list drag-and-drop
- Field editor property updates
- Form preview rendering
- Import dialog flow

### Integration Tests

- End-to-end import → edit → save → verify
- Load existing metadata → edit → save
- Validation error handling
- API error handling

### Manual Testing Checklist

- [ ] Import PP47 class successfully
- [ ] Import CAC12 class successfully
- [ ] Edit field properties and verify preview updates
- [ ] Reorder fields via drag-and-drop
- [ ] Add new field manually
- [ ] Delete field with confirmation
- [ ] Save metadata and verify in database
- [ ] Load saved metadata and verify all fields
- [ ] Test validation errors
- [ ] Test with missing JsonOptions reference
- [ ] Test GitHub import with valid profile
- [ ] Test GitHub import with invalid profile
- [ ] Test mobile responsive layout

---

## Security Considerations

1. **Authorization**: Only superusers can access editor
2. **Input Sanitization**: Validate all C# code input
3. **SQL Injection**: Use parameterized queries for save
4. **XSS Prevention**: Sanitize display names and help text
5. **Rate Limiting**: Limit GitHub API calls
6. **CSRF Protection**: Use anti-forgery tokens
7. **Audit Logging**: Track all metadata changes

---

## Performance Considerations

1. **Lazy Loading**: Only load editor when needed
2. **Debounce**: Debounce preview updates (300ms)
3. **Virtual Scrolling**: For large field lists (50+ fields)
4. **Caching**: Cache GitHub API responses (5 minutes)
5. **Optimistic Updates**: Update UI before API confirmation
6. **Background Save**: Save draft to localStorage while editing

---

## Dependencies

### Frontend
- Angular 19+
- Angular CDK (Drag & Drop)
- RxJS
- Bootstrap 5

### Backend
- ASP.NET Core 9
- Roslyn (C# parsing)
- HttpClient (GitHub API)
- Entity Framework Core

---

## Implementation Roadmap

### Sprint 1: Foundation (Week 1-2)
- [ ] Create component structure
- [ ] Design TypeScript models
- [ ] Build basic three-panel layout
- [ ] Implement field list component
- [ ] Add drag-and-drop functionality

### Sprint 2: Field Editor (Week 3)
- [ ] Build field properties panel
- [ ] Implement property validation
- [ ] Add conditional validation fields
- [ ] Create add/delete field logic

### Sprint 3: Import (Week 4)
- [ ] Build import dialog
- [ ] Implement backend C# parser (Roslyn)
- [ ] Create import API endpoint
- [ ] Test with sample PP/CAC classes

### Sprint 4: Preview & Save (Week 5)
- [ ] Build form preview component
- [ ] Implement live preview updates
- [ ] Create save API endpoint
- [ ] Add validation before save

### Sprint 5: GitHub Integration (Week 6)
- [ ] Implement GitHub API fetcher
- [ ] Create auto-import endpoint
- [ ] Add GitHub import UI
- [ ] Test with real TSIC-Unify repo

### Sprint 6: Polish (Week 7)
- [ ] Error handling and user feedback
- [ ] Accessibility improvements
- [ ] Mobile responsiveness
- [ ] User acceptance testing

---

## Open Questions

1. **GitHub Token**: Should we store GitHub personal access token in config or use GitHub App?
2. **Versioning**: Do we need to version metadata changes or just overwrite?
3. **Backup**: Should we backup previous metadata before overwrite?
4. **Multi-Job**: Can metadata be shared across multiple jobs?
5. **Permissions**: Should admin (non-superuser) have read-only access?

---

## Appendix

### Sample Generated Metadata

```json
{
  "profileName": "PP47",
  "registrationType": "PP",
  "teamConstraint": "BYGRADYEAR",
  "allowPayInFull": true,
  "fields": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440001",
      "name": "jerseySize",
      "dbColumn": "JerseySize",
      "displayName": "Jersey Size",
      "inputType": "select",
      "dataSource": "ListSizes_Jersey",
      "required": true,
      "order": 10,
      "adminOnly": false,
      "validation": {
        "required": true
      },
      "placeholder": "Select a size...",
      "helpText": "Choose the size that fits best"
    },
    {
      "id": "550e8400-e29b-41d4-a716-446655440002",
      "name": "specialRequests",
      "dbColumn": "SpecialRequests",
      "displayName": "Special Requests",
      "inputType": "textarea",
      "required": false,
      "order": 20,
      "adminOnly": false,
      "validation": {
        "maxLength": 500
      },
      "placeholder": "Enter any special requests or accommodations...",
      "helpText": "Medical conditions, carpools, etc."
    }
  ]
}
```

---

**End of Document**

*Ready for implementation. Please review and provide feedback.*
