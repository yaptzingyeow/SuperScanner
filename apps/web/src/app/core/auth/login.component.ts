import { I18nService } from '../i18n/i18n.service';
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
  protected readonly i18n = inject(I18nService);
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
      return this.i18n.t('pages.login.err.domain');
    if (code === 'auth/credential-already-in-use')
      return this.i18n.t('pages.login.err.inUse');
    if (code === 'auth/popup-closed-by-user') return this.i18n.t('pages.login.err.cancelled');
    if (code === 'auth/popup-blocked')
      return this.i18n.t('pages.login.err.popup');
    if (code === 'auth/account-exists-with-different-credential')
      return this.i18n.t('pages.login.err.different');
    if (code === 'auth/email-already-in-use') return this.i18n.t('pages.login.err.exists');
    if (code === 'auth/weak-password') return this.i18n.t('pages.login.err.weak');
    if (['auth/invalid-credential', 'auth/user-not-found', 'auth/wrong-password'].includes(code))
      return this.i18n.t('pages.login.err.invalid');
    if (code === 'auth/operation-not-allowed') return this.i18n.t('pages.login.err.notEnabled');
    if (code === 'auth/network-request-failed') return this.i18n.t('pages.login.err.network');
    return this.i18n.t('pages.login.err.generic');
  }
}
