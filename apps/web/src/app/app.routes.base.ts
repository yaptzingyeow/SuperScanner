import { Routes } from '@angular/router';
import { DocumentWorkspaceComponent } from './documents/document-workspace.component';
import { CropEditorComponent } from './documents/crop-editor.component';
import { ImportReviewComponent } from './documents/import-review.component';
import { PageCleanupComponent } from './documents/page-cleanup.component';
import { PageTextEditorComponent } from './documents/page-text-editor.component';
import { DocumentListComponent } from './documents/document-list.component';
import { NewDocumentComponent } from './documents/new-document.component';
import { UploadStatusComponent } from './documents/upload-status.component';
import { AppShellComponent } from './layout/app-shell.component';
import { authGuard, signedInGuard } from './core/auth/auth.guard';
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
      { path: 'admin', canActivate: [signedInGuard], loadChildren: () => import('./admin/admin.routes').then((m) => m.adminRoutes) },
      { path: 'documents/:documentId', component: DocumentWorkspaceComponent,
        canDeactivate: [(component: DocumentWorkspaceComponent) => component.canLeave()] },
      { path: 'documents/:documentId/import', component: ImportReviewComponent },
      { path: 'documents/:documentId/pages/:pageId/crop', component: CropEditorComponent },
      { path: 'documents/:documentId/pages/:pageId/clean', component: PageCleanupComponent },
      { path: 'documents/:documentId/pages/:pageId/text', component: PageTextEditorComponent,
        canActivate: [signedInGuard],
        canDeactivate: [(component: PageTextEditorComponent) => component.canLeave()] },
      {
        path: 'documents/:documentId/uploads/:uploadId',
        component: UploadStatusComponent,
      },
    ],
  },
];
