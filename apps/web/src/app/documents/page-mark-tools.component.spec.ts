import { TestBed } from '@angular/core/testing';
import { expect, it, vi } from 'vitest';
import { PageMarkToolsComponent } from './page-mark-tools.component';

it('offers accessible preset colors and emits the selected color without hiding custom color', () => {
  TestBed.configureTestingModule({ imports: [PageMarkToolsComponent] });
  const fixture = TestBed.createComponent(PageMarkToolsComponent);
  fixture.componentRef.setInput('draft', {
    id: 'draft', kind: 'Check', box: { x: .1, y: .2, width: .025, height: .025 },
    color: '#000000', strokeWidth: .08,
  });
  fixture.detectChanges();
  const chosen = vi.fn();
  fixture.componentInstance.color.subscribe(chosen);
  const presets = fixture.nativeElement.querySelectorAll('[data-testid^="mark-color-"]') as NodeListOf<HTMLButtonElement>;
  expect(presets).toHaveLength(4);
  expect(fixture.nativeElement.querySelector('input[type="color"]')).toBeTruthy();
  const blue = fixture.nativeElement.querySelector('[data-testid="mark-color-blue"]') as HTMLButtonElement;
  expect(blue.getAttribute('aria-label')).toBe('Blue mark');
  blue.click();
  expect(chosen).toHaveBeenCalledWith('#2563EB');
});
