# Angular Component Template (TSIC Design System)

Use this as a starting point for all new components. Copy/paste and modify as needed.

---

## **Standard Card Component**

```typescript
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-my-component',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './my-component.component.html',
  styleUrl: './my-component.component.scss'
})
export class MyComponentComponent {
  // Component logic here
}
```

```html
<!-- my-component.component.html -->
<div class="card shadow-sm">
  <div class="card-header bg-surface border-0 py-3">
    <h5 class="mb-0">Component Title</h5>
    <p class="text-muted small mb-0 mt-2">Optional description text</p>
  </div>
  <div class="card-body">
    <p class="mb-0">Card content goes here.</p>
  </div>
</div>
```

```scss
// my-component.component.scss
// Only add component-specific styles here, not design tokens
.my-component {
  // Component-specific overrides only
}
```

---

## **Dashboard Stat Card**

```html
<div class="card bg-primary-subtle">
  <div class="card-body">
    <h6 class="text-muted mb-2">Metric Name</h6>
    <h3 class="mb-0 text-primary">$12,450</h3>
    <small class="text-success">↑ 12% from last month</small>
  </div>
</div>
```

---

## **Form Component**

```html
<form class="card shadow-sm">
  <div class="card-header bg-surface border-0 py-3">
    <h5 class="mb-0">Form Title</h5>
  </div>
  <div class="card-body">
    <!-- Text Input -->
    <div class="mb-4">
      <label class="form-label font-medium">Email Address</label>
      <input 
        type="email" 
        class="form-control" 
        placeholder="you@example.com"
        required>
      <small class="form-text text-muted">We'll never share your email.</small>
    </div>

    <!-- Select Dropdown -->
    <div class="mb-4">
      <label class="form-label font-medium">Country</label>
      <select class="form-select">
        <option selected>Choose...</option>
        <option value="us">United States</option>
        <option value="ca">Canada</option>
      </select>
    </div>

    <!-- Checkbox -->
    <div class="mb-4 form-check">
      <input class="form-check-input" type="checkbox" id="terms">
      <label class="form-check-label" for="terms">
        I agree to the terms and conditions
      </label>
    </div>

    <!-- Action Buttons -->
    <div class="d-flex gap-2">
      <button type="submit" class="btn btn-primary">Submit</button>
      <button type="button" class="btn btn-outline-secondary">Cancel</button>
    </div>
  </div>
</form>
```

---

## **Table Component**

```html
<div class="card shadow-sm">
  <div class="card-header bg-surface border-0 py-3">
    <div class="d-flex justify-content-between align-items-center">
      <h5 class="mb-0">Recent Transactions</h5>
      <button class="btn btn-sm btn-outline-primary">View All</button>
    </div>
  </div>
  <div class="card-body p-0">
    <div class="table-responsive">
      <table class="table table-hover mb-0">
        <thead class="bg-neutral-50">
          <tr>
            <th class="font-semibold">ID</th>
            <th class="font-semibold">Name</th>
            <th class="font-semibold">Status</th>
            <th class="font-semibold text-end">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td class="text-muted">#001</td>
            <td>John Doe</td>
            <td><span class="badge bg-success-subtle text-success">Active</span></td>
            <td class="text-end">
              <button class="btn btn-sm btn-link text-primary">Edit</button>
            </td>
          </tr>
          <!-- More rows -->
        </tbody>
      </table>
    </div>
  </div>
</div>
```

---

## **Modal Component**

```html
<!-- Trigger Button -->
<button type="button" class="btn btn-primary" data-bs-toggle="modal" data-bs-target="#myModal">
  Open Modal
</button>

<!-- Modal -->
<div class="modal fade" id="myModal" tabindex="-1" aria-labelledby="myModalLabel" aria-hidden="true">
  <div class="modal-dialog modal-dialog-centered">
    <div class="modal-content shadow-xl">
      <div class="modal-header border-0">
        <h5 class="modal-title" id="myModalLabel">Modal Title</h5>
        <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
      </div>
      <div class="modal-body">
        <p class="text-muted mb-0">Modal content goes here.</p>
      </div>
      <div class="modal-footer border-0 bg-neutral-50">
        <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>
        <button type="button" class="btn btn-primary">Save Changes</button>
      </div>
    </div>
  </div>
</div>
```

---

## **Alert/Notification Component**

```html
<!-- Success Alert -->
<div class="alert bg-success-subtle border-0 d-flex align-items-center" role="alert">
  <i class="bi bi-check-circle-fill text-success me-3"></i>
  <div>
    <strong class="text-success">Success!</strong>
    <p class="mb-0 text-muted">Your changes have been saved.</p>
  </div>
</div>

<!-- Warning Alert -->
<div class="alert bg-warning-subtle border-0 d-flex align-items-center" role="alert">
  <i class="bi bi-exclamation-triangle-fill text-warning me-3"></i>
  <div>
    <strong class="text-warning">Warning!</strong>
    <p class="mb-0 text-muted">Please review your information.</p>
  </div>
</div>

<!-- Danger Alert -->
<div class="alert bg-danger-subtle border-0 d-flex align-items-center" role="alert">
  <i class="bi bi-x-circle-fill text-danger me-3"></i>
  <div>
    <strong class="text-danger">Error!</strong>
    <p class="mb-0 text-muted">Something went wrong.</p>
  </div>
</div>
```

---

## **List Group Component**

```html
<div class="card shadow-sm">
  <div class="card-header bg-surface border-0 py-3">
    <h5 class="mb-0">Recent Activity</h5>
  </div>
  <ul class="list-group list-group-flush">
    <li class="list-group-item d-flex justify-content-between align-items-center">
      <div>
        <strong>John Doe</strong> updated profile
        <div class="small text-muted">2 hours ago</div>
      </div>
      <span class="badge bg-primary-subtle text-primary rounded-full">New</span>
    </li>
    <li class="list-group-item d-flex justify-content-between align-items-center">
      <div>
        <strong>Jane Smith</strong> completed registration
        <div class="small text-muted">5 hours ago</div>
      </div>
    </li>
    <!-- More items -->
  </ul>
</div>
```

---

## **Empty State Component**

```html
<div class="card shadow-sm">
  <div class="card-body text-center py-8">
    <i class="bi bi-inbox text-muted" style="font-size: 3rem;"></i>
    <h5 class="mt-4 mb-2">No items found</h5>
    <p class="text-muted mb-4">Get started by creating your first item.</p>
    <button class="btn btn-primary">Create New Item</button>
  </div>
</div>
```

---

## **Checklist: Before Committing**

- [ ] Used CSS variables from `_tokens.scss` (no hardcoded colors/spacing)
- [ ] Applied appropriate background utilities (`.bg-surface`, `.bg-primary-subtle`)
- [ ] Used spacing scale (`.mb-4`, `.p-3`, etc.)
- [ ] Included focus states for keyboard navigation
- [ ] Tested with all 8 color palettes
- [ ] Responsive on mobile (375px), tablet (768px), desktop (1440px)
- [ ] Semantic HTML (`<button>`, `<label>`, `<header>`)
- [ ] No inline styles (use utility classes)
- [ ] Accessibility: `aria-label`, `role`, proper heading hierarchy

---

**Questions?** Review the [Design System Documentation](./DESIGN-SYSTEM.md) or the Brand Preview component.
