import { Component, EventEmitter, Input, Output } from '@angular/core';
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
  @Input({ required: true }) page!: DocumentPage;
  @Input({ required: true }) documentId = '';
  @Input() thumbnailUrl?: string;
  @Input() first = false;
  @Input() last = false;
  @Output() readonly movePage = new EventEmitter<-1 | 1>();
  @Output() readonly removePage = new EventEmitter<void>();
  @Output() readonly downloadOriginal = new EventEmitter<void>();

  protected stateLabel(state: string): string {
    return state.replace(/([a-z])([A-Z])/g, '$1 $2');
  }
}
