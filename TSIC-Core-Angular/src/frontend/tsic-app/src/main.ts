import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { registerLicense } from '@syncfusion/ej2-base';
import { environment } from './environments/environment';

assertEnvironmentMatches(environment.envName, environment.apiUrl);

// Mirrors backend [STARTUP-CONFIG] series — surfaces in the browser console so a
// build can be visually confirmed pointed at the right env after deploy.
console.info(
  `[STARTUP-CONFIG] env=${environment.envName} host=${window.location.hostname} apiUrl=${environment.apiUrl} staticsUrl=${environment.staticsUrl} build=${environment.buildVersion}`
);

// Drives env-aware chrome (header tint + chip in client-header-bar). Set on <body>
// so global SCSS in _elevated-components.scss can key off it without component scope.
document.body.dataset['env'] = environment.envName;

// Syncfusion unified license key (v33). registerLicense() runs client-side, so this
// ships in the browser bundle by design — it is version/domain-locked, not a credential.
// Paste the v33 "Select all" key below; keep it in lockstep with the @syncfusion package
// version (33.x) in package.json or Syncfusion components revert to a trial banner.
registerLicense('Ngo9BigBOggjHTQxAR8/V1JHaF5cWWdCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWXpcdHVRRWhcWUF/XUBWYEo=');

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));

function assertEnvironmentMatches(envName: string, apiUrl: string): void {
  const valid =
    (envName === 'development' && apiUrl.includes('localhost')) ||
    (envName === 'staging' && apiUrl.includes('devapi.teamsportsinfo.com')) ||
    (envName === 'production' && apiUrl.includes('claude-api.teamsportsinfo.com'));

  if (!valid) {
    const msg = `Environment mismatch: envName="${envName}" apiUrl="${apiUrl}". Refusing to bootstrap.`;
    document.body.innerHTML = `<pre style="padding:2rem;color:#b00;font-family:monospace;white-space:pre-wrap;">${msg}</pre>`;
    throw new Error(msg);
  }
}
