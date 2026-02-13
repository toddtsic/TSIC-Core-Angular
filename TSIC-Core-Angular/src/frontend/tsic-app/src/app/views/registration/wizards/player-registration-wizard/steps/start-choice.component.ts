import { ChangeDetectionStrategy, Component } from '@angular/core';

// StartChoice component retired. Selector retained temporarily as a no-op so any stale
// compiled bundles referencing it don't break. All direct template references have been removed.
// Will be removed permanently after the next clean build + deploy cycle confirms it's unused.
@Component({
  selector: 'app-rw-start-choice',
  standalone: true,
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StartChoiceComponent { }
