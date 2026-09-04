import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { securityInterceptor } from './core/api/security.interceptor';
import { provideFirebaseSecurity } from './core/auth/firebase.providers';
import { provideE2eSecurity } from './core/auth/e2e-auth.providers';
import { HttpSignedUploadClient, SIGNED_UPLOAD_CLIENT } from './documents/upload.service';
import { environment } from '../environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([securityInterceptor])),
    environment.e2e ? provideE2eSecurity(environment.apiBaseUrl) : provideFirebaseSecurity(),
    { provide: SIGNED_UPLOAD_CLIENT, useExisting: HttpSignedUploadClient },
  ],
};
