import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { AdminApiService } from './admin-api.service';
import { AdminMember } from './admin.models';

@Component({
  selector: 'app-admin-admins',
  imports: [DatePipe],
  styleUrl: './admin.scss',
  template: `
    <h1>{{ i18n.t('admin.admins.title') }}</h1>
    <form class="row panel" (submit)="$event.preventDefault(); add()">
      <label>{{ i18n.t('admin.admins.addLabel') }}
        <input data-admin-email type="email" placeholder="name@example.com" [value]="email()" (input)="email.set($any($event.target).value)" />
      </label>
      <button class="primary" type="submit" data-add-admin [disabled]="busy() || !email().trim()">{{ i18n.t('admin.admins.add') }}</button>
    </form>
    <p class="muted">{{ i18n.t('admin.admins.hint') }}</p>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    <div class="table-wrap panel">
      <table>
        <thead><tr><th>{{ i18n.t('admin.col.email') }}</th><th>{{ i18n.t('admin.admins.added') }}</th><th></th></tr></thead>
        <tbody>
          @for (admin of admins(); track admin.uid) {
            <tr>
              <td>{{ admin.email ?? admin.uid }}</td>
              <td>{{ admin.addedAt | date: 'mediumDate' }}</td>
              <td><button type="button" class="danger" data-remove-admin [disabled]="busy()" (click)="remove(admin)">{{ i18n.t('admin.admins.remove') }}</button></td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class AdminsComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AdminApiService);
  protected readonly admins = signal<AdminMember[]>([]);
  protected readonly email = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  ngOnInit(): void { void this.load(); }

  protected async add(): Promise<void> {
    await this.run(async () => {
      await this.api.addAdmin(this.email());
      this.email.set('');
    }, (status) => status === 404
      ? this.i18n.t('admin.admins.notFound')
      : this.i18n.t('admin.admins.addFailed'));
  }

  protected async remove(admin: AdminMember): Promise<void> {
    if (!window.confirm(this.i18n.t('admin.admins.removeConfirm', { name: admin.email ?? admin.uid }))) return;
    await this.run(() => this.api.removeAdmin(admin.uid), (status) => status === 409
      ? this.i18n.t('admin.admins.lastOne')
      : this.i18n.t('admin.admins.removeFailed'));
  }

  private async run(action: () => Promise<unknown>, failure: (status: number) => string): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
      await this.load();
    } catch (error) {
      this.error.set(failure(error instanceof HttpErrorResponse ? error.status : 0));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.admins.set((await this.api.admins()).items);
    } catch {
      this.error.set(this.i18n.t('admin.admins.loadFailed'));
    }
  }
}
