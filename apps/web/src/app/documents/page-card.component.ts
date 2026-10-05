import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { CdkDragHandle } from '@angular/cdk/drag-drop';
import { RouterLink } from '@angular/router';
import { DocumentPage } from './document.models';

@Component({
  selector: 'app-page-card',
  standalone: true,
  imports: [RouterLink, CdkDragHandle],
  templateUrl: './page-card.component.html',
  styleUrl: './page-card.component.scss',
})
export class PageCardComponent {
  protected readonly i18n = inject(I18nService);
  @Input({ required: true }) page!: DocumentPage;
  @Input({ required: true }) documentId = '';
  @Input() thumbnailUrl?: string;
  @Input() first = false;
  @Input() last = false;
  @Output() readonly movePage = new EventEmitter<-1 | 1>();
  @Output() readonly removePage = new EventEmitter<void>();
  @Output() readonly downloadOriginal = new EventEmitter<void>();

  protected stateLabel(state: string): string {
    const label = state.replace(/([a-z])([A-Z])/g, '$1 $2');
    const key = 'pages.state.' + label.replace(/ /g, '');
    const text = this.i18n.t(key);
    return text === key ? label : text;
  }
}
