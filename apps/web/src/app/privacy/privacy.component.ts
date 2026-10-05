import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { AuthService } from '../core/auth/auth.service';

/** The person's own data: download everything, or delete the account. */
@Component({
  selector: 'app-privacy',
  template: `
    <section class="privacy">
      <h1>Privacy &amp; your data</h1>
      <p class="lead">Your documents are private to you. You can take a copy of your data or delete your account at any time.</p>

      <article class="card">
        <h2>Download my data</h2>
        <p>A file with your account details, documents and their recognized text, plan usage and activity.</p>
        <button type="button" class="primary" data-download-data [disabled]="busy()" (click)="download()">Download my data</button>
      </article>

      <article class="card card--danger">
        <h2>Delete my account</h2>
        <p>This removes your documents and your sign-in. It cannot be undone from the app.</p>
        <label>Type <strong>DELETE</strong> to confirm
          <input data-delete-confirm autocomplete="off" [value]="confirmText()" (input)="confirmText.set($any($event.target).value)" />
        </label>
        <button type="button" class="danger" data-delete-account [disabled]="busy() || confirmText() !== 'DELETE'"
          (click)="deleteAccount()">Delete my account</button>
      </article>

      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      @if (notice()) { <p class="ok" role="status">{{ notice() }}</p> }
    </section>
  `,
  styles: [`
    .privacy { max-width: 720px; margin: 0 auto; padding: 24px 16px 48px; }
    h1 { font-family: 'Source Serif 4', Georgia, serif; font-size: 1.8rem; margin: 0 0 8px; }
    .lead { color: var(--color-text-muted, #737b76); margin: 0 0 20px; }
    .card { background: #fff; border: 1px solid #e3e5df; border-radius: 12px; padding: 18px; margin-bottom: 16px; display: grid; gap: 10px; }
    .card h2 { margin: 0; font-size: 1.05rem; }
    .card p { margin: 0; color: #3f4a44; }
    .card--danger { border-color: #f0c9c5; }
    label { display: grid; gap: 6px; font-size: .9rem; }
    input { min-height: 40px; padding: 0 10px; border: 1px solid #cbd2ca; border-radius: 8px; font: inherit; max-width: 260px; }
    button { justify-self: start; min-height: 42px; padding: 0 16px; border-radius: 10px; font: inherit; font-weight: 600; cursor: pointer; border: 1px solid transparent; }
    .primary { background: #1e6b50; color: #fff; }
    .danger { background: #fff; color: #ac3d39; border-color: #ac3d39; }
    button:disabled { opacity: .5; cursor: default; }
    .error { color: #ac3d39; font-weight: 600; }
    .ok { color: #176047; font-weight: 600; }
  `],
})
export class PrivacyComponent {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  protected readonly busy = signal(false);
  protected readonly confirmText = signal('');
  protected readonly error = signal('');
  protected readonly notice = signal('');

  /** Saves a blob as a download (replaceable in tests). */
  protected saveFile = (blob: Blob, name: string): void => {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };

  protected async download(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const data = await firstValueFrom(this.http.get(`${this.base}/me/export`));
      this.saveFile(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }), 'arks-scanner-my-data.json');
      this.notice.set('Your data file has been downloaded.');
    } catch {
      this.error.set('We could not prepare your data. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async deleteAccount(): Promise<void> {
    if (this.confirmText() !== 'DELETE') return;
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.http.delete(`${this.base}/me`, { body: { confirm: 'DELETE' } }));
      await this.auth.signOut();
      await this.auth.ensureGuest();
      await this.router.navigateByUrl('/');
    } catch (error) {
      this.error.set(error instanceof HttpErrorResponse && error.status === 409
        ? 'You are the only admin. Add another admin before deleting your account.'
        : 'Your account could not be deleted. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
