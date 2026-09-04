import { Routes } from '@angular/router';
import { DocumentListComponent } from './documents/document-list.component';
import { AppShellComponent } from './layout/app-shell.component';

export const routes: Routes = [
  {
    path: '',
    component: AppShellComponent,
    children: [{ path: '', component: DocumentListComponent }],
  },
];
