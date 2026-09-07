import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected email = '';
  protected password = '';
  protected readonly createMode = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected toggleMode(): void {
    this.createMode.update((value) => !value);
    this.error.set('');
  }

  protected async submit(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      if (this.createMode()) await this.auth.createAccount(this.email, this.password);
      else await this.auth.signIn(this.email, this.password);
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      await this.router.navigateByUrl(
        returnUrl?.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/',
      );
    } catch (error: unknown) {
      this.error.set(this.toMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  private toMessage(error: unknown): string {
    const code = typeof error === 'object' && error !== null && 'code' in error ? String(error.code) : '';
    if (code === 'auth/email-already-in-use') return 'An account already exists for this email.';
    if (code === 'auth/weak-password') return 'Choose a stronger password with at least 6 characters.';
    if (['auth/invalid-credential', 'auth/user-not-found', 'auth/wrong-password'].includes(code)) return 'The email or password is incorrect.';
    if (code === 'auth/network-request-failed') return 'Network unavailable. Please try again.';
    return 'We could not complete sign in. Please try again.';
  }
}
