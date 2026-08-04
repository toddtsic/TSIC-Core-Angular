import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterModule } from '@angular/router';

@Component({
    selector: 'app-scheduling-shell',
    standalone: true,
    imports: [RouterModule],
    templateUrl: './scheduling-shell.component.html',
    styleUrl: './scheduling-shell.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class SchedulingShellComponent {}
