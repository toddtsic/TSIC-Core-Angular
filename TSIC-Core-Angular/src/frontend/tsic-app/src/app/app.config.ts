import { ApplicationConfig, APP_INITIALIZER, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { tokenRefreshInterceptor } from './core/interceptors/token-refresh.interceptor';

import { routes } from './app.routes';
import { LastLocationService } from './core/services/last-location.service';
import { ThemeOverridesService } from './core/services/theme-overrides.service';
import { JobContextService } from './core/services/job-context.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([authInterceptor, tokenRefreshInterceptor])
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
    // Initialize JobContextService early so jobPath is available to components/guards
    {
      provide: APP_INITIALIZER,
      deps: [JobContextService],
      useFactory: (svc: JobContextService) => () => svc.init(),
      multi: true
    }
  ]
};
