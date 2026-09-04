import { Component, inject, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { API_BASE_URL } from '../api/security.interceptor';
import { E2eIdentityStore, E2eIdentityTokens } from './e2e-auth.providers';

@Component({
  selector: 'app-e2e-login',
  template: '<p role="status">Preparing secure test identity…</p>',
})
export class E2eLoginComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly identity = inject(E2eIdentityStore);
  private readonly apiBaseUrl = inject(API_BASE_URL).replace(/\/+$/, '');

  async ngOnInit(): Promise<void> {
    const user = this.route.snapshot.queryParamMap.get('user');
    if (user !== 'user-a' && user !== 'user-b') {
      throw new Error('Unsupported E2E identity.');
    }

    const response = await fetch(
      `${this.apiBaseUrl}/e2e/identity?user=${encodeURIComponent(user)}`,
      { credentials: 'omit', headers: { Accept: 'application/json' } },
    );
    if (!response.ok) throw new Error('E2E identity fixture is unavailable.');

    this.identity.set((await response.json()) as E2eIdentityTokens);
    await this.router.navigateByUrl('/');
  }
}
