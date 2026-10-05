import { Injectable, TemplateRef, signal } from '@angular/core';

/**
 * Lets the embedded page editor show its controls in the workspace's right-hand panel
 * (e.g. Add text styling) while the text box itself is drawn on the page.
 */
@Injectable({ providedIn: 'root' })
export class SidePanelSlotService {
  readonly template = signal<TemplateRef<unknown> | null>(null);
  show(template: TemplateRef<unknown>): void { this.template.set(template); }
  clear(template?: TemplateRef<unknown>): void {
    if (!template || this.template() === template) this.template.set(null);
  }
}
