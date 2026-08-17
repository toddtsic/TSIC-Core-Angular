import { ApplicationConfig, inject, provideAppInitializer, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter, withInMemoryScrolling, withNavigationErrorHandler, withRouterConfig } from '@angular/router';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { authInterceptor } from './infrastructure/interceptors/auth.interceptor';

import { routes } from './app.routes';
import { chunkLoadRecoveryHandler } from './infrastructure/navigation/chunk-load-recovery';
import { AppVersionService } from './infrastructure/services/app-version.service';
import { LastLocationService } from './infrastructure/services/last-location.service';
import { ThemeOverridesService } from './infrastructure/services/theme-overrides.service';
import { JobContextService } from './infrastructure/services/job-context.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(
      routes,
      withRouterConfig({ onSameUrlNavigation: 'ignore', paramsInheritanceStrategy: 'emptyOnly' }),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
      // Backstop for a click that lands inside a deploy's copy window: a lazy route chunk the
      // publish deleted surfaces here as a NavigationError; reload to fetch fresh hashes.
      // See infrastructure/navigation/chunk-load-recovery.
      withNavigationErrorHandler(chunkLoadRecoveryHandler)
    ),
    provideHttpClient(withXhr(),
      withInterceptors([authInterceptor])
    ),
    // "After I deploy, users get the new code": compare the served build stamp to ours on every
    // URL change and reload once when it differs. See infrastructure/services/app-version.service.
    provideAppInitializer(() => inject(AppVersionService).start()),
    // Ensure LastLocationService is instantiated at startup to begin tracking
    provideAppInitializer(() => { inject(LastLocationService); }),
    // Instantiate ThemeOverridesService to auto-apply saved per-job theme tokens
    provideAppInitializer(() => { inject(ThemeOverridesService); }),
    // Initialize JobContextService early so jobPath is available to components/guards
    provideAppInitializer(() => inject(JobContextService).init())
  ]
};
