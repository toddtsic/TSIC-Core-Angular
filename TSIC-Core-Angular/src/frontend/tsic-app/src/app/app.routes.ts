
import { Routes } from '@angular/router';

export const routes: Routes = [
	{ path: '', loadComponent: () => import('./login/login.component').then(m => m.LoginComponent) },
	{ path: 'role-selection', loadComponent: () => import('./role-selection/role-selection.component').then(m => m.RoleSelectionComponent) },
	{ path: 'job/:regId', loadComponent: () => import('./job/job.component').then(m => m.JobComponent) },
];
