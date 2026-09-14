import { Routes } from '@angular/router';
import { DocumentDetailComponent } from './documents/document-detail.component';
import { CropEditorComponent } from './documents/crop-editor.component';
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
      { path: '', component: NewDocumentComponent },
      { path: 'documents', component: DocumentListComponent },
      { path: 'documents/:documentId', component: DocumentDetailComponent },
      { path: 'documents/:documentId/pages/:pageId/crop', component: CropEditorComponent },
      {
        path: 'documents/:documentId/uploads/:uploadId',
        component: UploadStatusComponent,
      },
    ],
  },
];
