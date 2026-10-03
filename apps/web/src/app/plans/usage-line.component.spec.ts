import { TestBed } from '@angular/core/testing';
import { UsageLineComponent } from './usage-line.component';

describe('UsageLineComponent', () => {
  it('shows used / limit and nothing when unlimited', () => {
    const fixture = TestBed.createComponent(UsageLineComponent);
    fixture.componentRef.setInput('label', 'OCR today');
    fixture.componentRef.setInput('meter', { used: 2, limit: 5 });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('OCR today: 2 / 5');

    fixture.componentRef.setInput('meter', null);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('');
  });
});
