import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminApiService } from './admin-api.service';
import { AdminPage, AdminUser } from './admin.models';

@Component({
  selector: 'app-admin-users',
  imports: [DatePipe, RouterLink],
  styleUrl: './admin.scss',
  template: `
    <h1>Users</h1>
    <form class="row" (submit)="$event.preventDefault(); search()">
      <input data-user-search type="search" placeholder="Search by email or UID" aria-label="Search users"
        [value]="query()" (input)="query.set($any($event.target).value)" />
      <button data-search type="submit">Search</button>
    </form>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (result(); as r) {
      <p class="muted">{{ r.total }} {{ r.total === 1 ? 'user' : 'users' }}</p>
      <div class="table-wrap panel">
        <table>
          <thead><tr><th>Email</th><th>Sign-in</th><th>Plan</th><th>Joined</th><th>Last seen</th></tr></thead>
          <tbody>
            @for (user of r.items; track user.uid) {
              <tr>
                <td><a [routerLink]="['/admin/users', user.uid]">{{ user.email ?? (user.isGuest ? 'Guest' : user.uid) }}</a></td>
                <td>{{ user.provider }}</td>
                <td><span class="pill" [class.pill--pro]="user.plan === 'Pro'">{{ user.plan }}</span></td>
                <td>{{ user.createdAt | date: 'mediumDate' }}</td>
                <td>{{ user.lastSeenAt | date: 'short' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="empty">No users match.</td></tr>
            }
          </tbody>
        </table>
      </div>
      <div class="row pager">
        <button type="button" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</button>
        <span class="muted">Page {{ page() }}</span>
        <button type="button" [disabled]="page() * r.pageSize >= r.total" (click)="go(page() + 1)">Next</button>
      </div>
    }
  `,
})
export class UsersComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  protected readonly query = signal('');
  protected readonly page = signal(1);
  protected readonly result = signal<AdminPage<AdminUser> | null>(null);
  protected readonly error = signal('');

  ngOnInit(): void { void this.load(); }

  protected search(): void { this.page.set(1); void this.load(); }

  protected go(page: number): void { this.page.set(page); void this.load(); }

  private async load(): Promise<void> {
    this.error.set('');
    try {
      this.result.set(await this.api.users(this.query().trim(), this.page()));
    } catch {
      this.error.set('Could not load users.');
    }
  }
}
