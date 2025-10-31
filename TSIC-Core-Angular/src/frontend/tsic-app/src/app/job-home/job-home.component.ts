import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-job-home',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './job-home.component.html',
  styleUrl: './job-home.component.scss'
})
export class JobHomeComponent {
  // Job home content - header/navigation handled by LayoutComponent
}
