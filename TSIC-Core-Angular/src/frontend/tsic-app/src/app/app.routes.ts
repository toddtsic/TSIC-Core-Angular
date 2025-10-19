
import { Routes } from '@angular/router';

export const routes: Routes = [
	{ path: '', loadComponent: () => import('./login/login.component').then(m => m.LoginComponent) },
	{ path: 'job/:regId', loadComponent: () => import('./job/job.component').then(m => m.JobComponent) },
];
