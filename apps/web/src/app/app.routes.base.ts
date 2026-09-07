import { Routes } from '@angular/router';
import { DocumentListComponent } from './documents/document-list.component';
import { NewDocumentComponent } from './documents/new-document.component';
import { UploadStatusComponent } from './documents/upload-status.component';
import { AppShellComponent } from './layout/app-shell.component';
import { authGuard } from './core/auth/auth.guard';
import { LoginComponent } from './core/auth/login.component';

export const foundationRoutes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: '',
    component: AppShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', component: DocumentListComponent },
      { path: 'scan', component: NewDocumentComponent },
      {
        path: 'documents/:documentId/uploads/:uploadId',
        component: UploadStatusComponent,
      },
    ],
  },
];
