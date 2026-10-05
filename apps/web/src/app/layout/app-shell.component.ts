import { AsyncPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { PlanService } from '../plans/plan.service';
import { I18nService, LanguageCode } from '../core/i18n/i18n.service';

@Component({
  selector: 'app-shell',
  imports: [AsyncPipe, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly plans = inject(PlanService);
  protected readonly i18n = inject(I18nService);

  protected chooseLanguage(code: string): void {
    this.i18n.use(code as LanguageCode);
  }
  protected readonly menuOpen = signal(false);

  constructor() {
    void this.plans.refresh();
  }

  protected acceptPrivacy(): void {
    void this.plans.acceptPrivacy();
  }

  protected toggleMenu(): void {
    this.menuOpen.update((value) => !value);
    if (this.menuOpen()) void this.plans.refresh();
  }

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.auth.ensureGuest();
    await this.router.navigateByUrl('/');
  }
}
