import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { registerLicense } from '@syncfusion/ej2-base';

// Register Syncfusion license
registerLicense('@33322e302e303b33323bUCPckHHAnR1xq2UoiUbTi/3c47Cd2E20mhEiyalVDEo=');

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
