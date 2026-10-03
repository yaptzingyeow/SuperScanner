import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { API_BASE_URL } from '../core/api/security.interceptor';

/** Renders an admin page against a fake HTTP backend. */
export function renderAdmin<T>(component: Type<T>, params: Record<string, string> = {}) {
  TestBed.configureTestingModule({
    imports: [component],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: API_BASE_URL, useValue: '/api' },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params) } } },
    ],
  });
  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  return { fixture, http: TestBed.inject(HttpTestingController), root: fixture.nativeElement as HTMLElement };
}

export async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }): Promise<void> {
  for (let i = 0; i < 3; i++) { await fixture.whenStable(); await Promise.resolve(); fixture.detectChanges(); }
}
