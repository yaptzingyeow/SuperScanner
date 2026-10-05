import { Component, inject } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

/** The admin portal frame: a left menu and the selected page. */
@Component({
  selector: 'app-admin-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="admin">
      <nav class="admin__menu" [attr.aria-label]="i18n.t('admin.nav.label')">
        <strong class="admin__title">{{ i18n.t('admin.nav.title') }}</strong>
        @for (link of links; track link.path) {
          <a [routerLink]="link.path" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: link.path === '/admin' }">{{ i18n.t(link.label) }}</a>
        }
      </nav>
      <section class="admin__page"><router-outlet /></section>
    </div>
  `,
  styles: [`
    .admin { display: grid; grid-template-columns: 200px 1fr; gap: 20px; padding: 20px; max-width: 1200px; margin: 0 auto; }
    .admin__menu { display: grid; align-content: start; gap: 4px; }
    .admin__title { font-size: 11px; letter-spacing: .07em; text-transform: uppercase; color: var(--color-text-muted, #737b76); padding: 0 10px 6px; }
    .admin__menu a { padding: 9px 10px; border-radius: 8px; color: var(--color-text, #202d29); text-decoration: none; }
    .admin__menu a:hover { background: #f2f4ef; }
    .admin__menu a.active { background: #e3f0e9; color: #14503b; font-weight: 600; }
    .admin__page { min-width: 0; }
    @media (max-width: 720px) {
      .admin { grid-template-columns: 1fr; padding: 12px 16px; }
      .admin__menu { grid-auto-flow: column; overflow-x: auto; }
      .admin__title { display: none; }
    }
  `],
})
export class AdminShellComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly links = [
    { path: '/admin', label: 'admin.nav.dashboard' },
    { path: '/admin/users', label: 'admin.nav.users' },
    { path: '/admin/subscribers', label: 'admin.nav.subscribers' },
    { path: '/admin/payments', label: 'admin.nav.payments' },
    { path: '/admin/settings', label: 'admin.nav.settings' },
    { path: '/admin/admins', label: 'admin.nav.admins' },
  ];
}
