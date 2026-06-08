import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withInMemoryScrolling, withRouterConfig } from '@angular/router';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { authInterceptor } from './infrastructure/interceptors/auth.interceptor';

import { routes } from './app.routes';
import { LastLocationService } from './infrastructure/services/last-location.service';
import { PaletteService } from './infrastructure/services/palette.service';
import { ThemeOverridesService } from './infrastructure/services/theme-overrides.service';
import { JobContextService } from './infrastructure/services/job-context.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(
      routes,
      withRouterConfig({ onSameUrlNavigation: 'ignore', paramsInheritanceStrategy: 'emptyOnly' }),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' })
    ),
    provideHttpClient(withXhr(), 
      withInterceptors([authInterceptor])
    ),
    // Ensure LastLocationService is instantiated at startup to begin tracking
    provideAppInitializer(() => { inject(LastLocationService); }),
    // Instantiate ThemeOverridesService to auto-apply saved per-job theme tokens
    provideAppInitializer(() => { inject(ThemeOverridesService); }),
    // Apply saved palette on startup so colors persist across navigation
    provideAppInitializer(() => { inject(PaletteService); }),
    // Initialize JobContextService early so jobPath is available to components/guards
    provideAppInitializer(() => inject(JobContextService).init())
  ]
};
