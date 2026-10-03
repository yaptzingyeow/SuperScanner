import { Routes } from '@angular/router';
import { adminGuard } from './admin.guard';
import { AdminShellComponent } from './admin-shell.component';

export const adminRoutes: Routes = [
  {
    path: '',
    component: AdminShellComponent,
    canActivate: [adminGuard],
    children: [
      { path: '', loadComponent: () => import('./dashboard.component').then((m) => m.DashboardComponent) },
      { path: 'users', loadComponent: () => import('./users.component').then((m) => m.UsersComponent) },
      { path: 'users/:uid', loadComponent: () => import('./user-detail.component').then((m) => m.UserDetailComponent) },
      { path: 'subscribers', loadComponent: () => import('./subscribers.component').then((m) => m.SubscribersComponent) },
      { path: 'payments', loadComponent: () => import('./payments.component').then((m) => m.PaymentsComponent) },
      { path: 'settings', loadComponent: () => import('./settings.component').then((m) => m.SettingsComponent) },
      { path: 'admins', loadComponent: () => import('./admins.component').then((m) => m.AdminsComponent) },
    ],
  },
];
