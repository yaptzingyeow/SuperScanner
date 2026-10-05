import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { registerLocaleData } from '@angular/common';
import localeAr from '@angular/common/locales/ar';
import localeMs from '@angular/common/locales/ms';
import localeZh from '@angular/common/locales/zh';
import { ApplicationConfig, LOCALE_ID, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { securityInterceptor } from './core/api/security.interceptor';
import { provideFirebaseSecurity } from './core/auth/firebase.providers';
import { provideE2eSecurity } from './core/auth/e2e-auth.providers';
import { HttpSignedUploadClient, SIGNED_UPLOAD_CLIENT } from './documents/upload.service';
import { environment } from '../environments/environment';
import { I18nService, localeFor } from './core/i18n/i18n.service';

registerLocaleData(localeMs);
registerLocaleData(localeZh);
registerLocaleData(localeAr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([securityInterceptor])),
    environment.e2e ? provideE2eSecurity(environment.apiBaseUrl) : provideFirebaseSecurity(),
    { provide: SIGNED_UPLOAD_CLIENT, useExisting: HttpSignedUploadClient },
    // The chosen language's text loads before the first screen (English is built in).
    provideAppInitializer(() => inject(I18nService).ready()),
    // Dates and numbers follow the chosen app language.
    { provide: LOCALE_ID, useFactory: () => localeFor(inject(I18nService).language()) },
  ],
};
