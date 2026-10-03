import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { AdminApiService } from './admin-api.service';
import { AdminMember } from './admin.models';

@Component({
  selector: 'app-admin-admins',
  imports: [DatePipe],
  styleUrl: './admin.scss',
  template: `
    <h1>Admins</h1>
    <form class="row panel" (submit)="$event.preventDefault(); add()">
      <label>Add an admin by email
        <input data-admin-email type="email" placeholder="name@example.com" [value]="email()" (input)="email.set($any($event.target).value)" />
      </label>
      <button class="primary" type="submit" data-add-admin [disabled]="busy() || !email().trim()">Add admin</button>
    </form>
    <p class="muted">The person must have signed in to Arks Scanner at least once.</p>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    <div class="table-wrap panel">
      <table>
        <thead><tr><th>Email</th><th>Added</th><th></th></tr></thead>
        <tbody>
          @for (admin of admins(); track admin.uid) {
            <tr>
              <td>{{ admin.email ?? admin.uid }}</td>
              <td>{{ admin.addedAt | date: 'mediumDate' }}</td>
              <td><button type="button" class="danger" data-remove-admin [disabled]="busy()" (click)="remove(admin)">Remove</button></td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class AdminsComponent implements OnInit {
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
      ? 'No signed-in account uses that email yet. Ask them to sign in first.'
      : 'Could not add that admin.');
  }

  protected async remove(admin: AdminMember): Promise<void> {
    if (!window.confirm(`Remove ${admin.email ?? admin.uid} as an admin?`)) return;
    await this.run(() => this.api.removeAdmin(admin.uid), (status) => status === 409
      ? 'At least one admin must remain.'
      : 'Could not remove that admin.');
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
      this.error.set('Could not load admins.');
    }
  }
}
