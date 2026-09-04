import { Routes } from '@angular/router';
import { DocumentListComponent } from './documents/document-list.component';
import { NewDocumentComponent } from './documents/new-document.component';
import { UploadStatusComponent } from './documents/upload-status.component';
import { AppShellComponent } from './layout/app-shell.component';

export const routes: Routes = [
  {
    path: '',
    component: AppShellComponent,
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
