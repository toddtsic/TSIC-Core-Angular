import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { registerLicense } from '@syncfusion/ej2-base';
import { environment } from './environments/environment';

assertEnvironmentMatches(environment.envName, environment.apiUrl);

// Drives env-aware chrome (header tint + chip in client-header-bar). Set on <body>
// so global SCSS in _elevated-components.scss can key off it without component scope.
document.body.dataset['env'] = environment.envName;

registerLicense('Ngo9BigBOggjHTQxAR8/V1JHaF5cWWdCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWXtccHZUQ2JeWER2XERWYEo=');

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));

function assertEnvironmentMatches(envName: string, apiUrl: string): void {
  const valid =
    (envName === 'development' && apiUrl.includes('localhost')) ||
    (envName === 'staging'     && apiUrl.includes('devapi.teamsportsinfo.com')) ||
    (envName === 'production'  && apiUrl.includes('claude-api.teamsportsinfo.com'));

  if (!valid) {
    const msg = `Environment mismatch: envName="${envName}" apiUrl="${apiUrl}". Refusing to bootstrap.`;
    document.body.innerHTML = `<pre style="padding:2rem;color:#b00;font-family:monospace;white-space:pre-wrap;">${msg}</pre>`;
    throw new Error(msg);
  }
}
