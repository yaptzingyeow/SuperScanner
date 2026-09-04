import { Routes } from '@angular/router';
import { foundationRoutes } from './app.routes.base';
import { E2eLoginComponent } from './core/auth/e2e-login.component';

export const routes: Routes = [
  { path: 'e2e-login', component: E2eLoginComponent },
  ...foundationRoutes,
];
