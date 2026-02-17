import { ApplicationConfig, APP_INITIALIZER, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withRouterConfig } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
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
      withRouterConfig({ onSameUrlNavigation: 'ignore' })
    ),
    provideHttpClient(
      withInterceptors([authInterceptor])
    ),
    // Ensure LastLocationService is instantiated at startup to begin tracking
    {
      provide: APP_INITIALIZER,
      deps: [LastLocationService],
      useFactory: (svc: LastLocationService) => () => void 0,
      multi: true
    },
    // Instantiate ThemeOverridesService to auto-apply saved per-job theme tokens
    {
      provide: APP_INITIALIZER,
      deps: [ThemeOverridesService],
      useFactory: (svc: ThemeOverridesService) => () => void 0,
      multi: true
    },
    // Apply saved palette on startup so colors persist across navigation
    {
      provide: APP_INITIALIZER,
      deps: [PaletteService],
      useFactory: (svc: PaletteService) => () => void 0,
      multi: true
    },
    // Initialize JobContextService early so jobPath is available to components/guards
    {
      provide: APP_INITIALIZER,
      deps: [JobContextService],
      useFactory: (svc: JobContextService) => () => svc.init(),
      multi: true
    }
  ]
};
