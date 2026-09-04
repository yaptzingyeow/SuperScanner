import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppShellComponent } from './app-shell.component';

describe('AppShellComponent', () => {
  it('renders the SuperScanner navigation and accessible application landmarks', () => {
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [provideRouter([])],
    });
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-brand]')?.textContent).toContain('SuperScanner');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Home');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Scan');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Edit');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Export');
    expect(root.querySelector('main')).not.toBeNull();
    expect(root.querySelector('main router-outlet')).not.toBeNull();
    expect(root.querySelector('button[aria-label="Open account menu"]')).not.toBeNull();
  });
});
