import { Component, input } from '@angular/core';
import { UsageMeter } from './plan.models';

/** "OCR today: 2 / 5" — renders nothing when the plan has no limit. */
@Component({
  selector: 'app-usage-line',
  standalone: true,
  template: `@if (meter(); as m) {<span class="usage-line" [class.usage-line--full]="m.used >= m.limit">{{ label() }}: {{ m.used }} / {{ m.limit }}</span>}`,
  styles: [`.usage-line { font-size: .8rem; color: var(--muted, #667085); } .usage-line--full { color: var(--danger, #b42318); font-weight: 600; }`],
})
export class UsageLineComponent {
  readonly label = input.required<string>();
  readonly meter = input<UsageMeter | null>(null);
}
