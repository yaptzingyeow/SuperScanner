import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly registerMode = signal(false);
  protected email = '';
  protected password = '';

  protected signInWithGoogle(): Promise<void> {
    return this.signIn(() => this.auth.signInWithGoogle());
  }

  protected submitEmail(): Promise<void> {
    return this.signIn(() =>
      this.registerMode()
        ? this.auth.registerWithEmail(this.email, this.password)
        : this.auth.signInWithEmail(this.email, this.password),
    );
  }

  protected toggleMode(): void {
    this.registerMode.update((value) => !value);
    this.error.set('');
  }

  private async signIn(action: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
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
    const code =
      typeof error === 'object' && error !== null && 'code' in error ? String(error.code) : '';
    // Record only the error code: Firebase errors can contain credentials and personal data.
    console.warn('Sign-in failed:', /^auth\/[a-z0-9-]+$/.test(code) ? code : 'unknown');
    if (code === 'auth/unauthorized-domain')
      return 'Sign-in is not configured for this website address. Please contact support.';
    if (code === 'auth/credential-already-in-use')
      return 'This Google account already has a workspace. Sign in to that existing account to continue.';
    if (code === 'auth/popup-closed-by-user') return 'Sign-in was cancelled.';
    if (code === 'auth/popup-blocked')
      return 'Your browser blocked the sign-in window. Please allow popups and try again.';
    if (code === 'auth/account-exists-with-different-credential')
      return 'This email already uses another sign-in method.';
    if (code === 'auth/email-already-in-use') return 'An account already exists for this email.';
    if (code === 'auth/weak-password') return 'Use a password with at least 6 characters.';
    if (['auth/invalid-credential', 'auth/user-not-found', 'auth/wrong-password'].includes(code))
      return 'The email or password is incorrect.';
    if (code === 'auth/operation-not-allowed') return 'This sign-in method is not enabled yet.';
    if (code === 'auth/network-request-failed') return 'Network unavailable. Please try again.';
    return 'We could not sign you in. Please try again.';
  }
}
