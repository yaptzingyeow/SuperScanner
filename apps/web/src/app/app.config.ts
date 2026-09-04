import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { securityInterceptor } from './core/api/security.interceptor';
import { provideFirebaseSecurity } from './core/auth/firebase.providers';
import { HttpSignedUploadClient, SIGNED_UPLOAD_CLIENT } from './documents/upload.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([securityInterceptor])),
    provideFirebaseSecurity(),
    { provide: SIGNED_UPLOAD_CLIENT, useExisting: HttpSignedUploadClient },
  ],
};
