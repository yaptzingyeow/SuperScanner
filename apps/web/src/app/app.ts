import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationCancel, NavigationEnd, NavigationError, Router, RouterOutlet } from '@angular/router';
import { filter, map, take } from 'rxjs';
import { I18nService } from './core/i18n/i18n.service';

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  protected readonly i18n = inject(I18nService);
  /** False until the first navigation settles (sign-in is checked before the first page shows). */
  protected readonly ready = toSignal(inject(Router).events.pipe(
    filter((event) => event instanceof NavigationEnd || event instanceof NavigationCancel || event instanceof NavigationError),
    take(1), map(() => true)), { initialValue: false });
}
