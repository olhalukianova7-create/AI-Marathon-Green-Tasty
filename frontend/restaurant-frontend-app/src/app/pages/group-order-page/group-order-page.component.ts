import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import {
  catchError,
  defer,
  EMPTY,
  exhaustMap,
  finalize,
  forkJoin,
  interval,
  map,
  Observable,
  switchMap,
  tap,
} from 'rxjs';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { GroupReservationStatus } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';

@Component({
  selector: 'app-group-order-page',
  imports: [CurrencyPipe, DatePipe, ParticipantCardComponent, ModalComponent],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent implements OnInit {
  private readonly service = inject(GroupOrdersService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';

  readonly invite = signal<GuestInvite | null>(null);
  readonly participants = signal<Participant[]>([]);
  readonly ownerSessionId = signal('');
  readonly status = signal<GroupReservationStatus>('Open');
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly submitError = signal(false);
  readonly confirmationOpen = signal(false);
  readonly removing = signal(false);
  readonly removeTarget = signal<Participant | null>(null);
  readonly removeError = signal(false);
  readonly syncing = signal(false);
  readonly syncError = signal(false);
  readonly lastUpdated = signal<Date | null>(null);

  private readonly synchronization = effect((onCleanup) => {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || this.status() !== 'Open' || this.loading() || this.loadError()) return;

    const subscription = interval(15_000)
      .pipe(exhaustMap(() => this.refreshSnapshot$(groupId)))
      .subscribe();
    onCleanup(() => subscription.unsubscribe());
  });

  readonly participantCount = computed(() => this.participants().length);
  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  readonly grandTotal = computed(() => this.consolidated().total);
  readonly participantsTotal = computed(() =>
    this.participants().reduce((total, person) => total + person.total, 0)
  );
  readonly hasDishes = computed(() =>
    this.participants().some((person) => person.items.length > 0)
  );
  readonly pendingCount = computed(
    () => this.participants().filter((person) => !person.isSubmitted).length
  );
  readonly pendingMessage = computed(() => {
    const count = this.pendingCount();
    const subject = count === 1 ? "1 participant hasn't" : `${count} participants haven't`;
    return `${subject} submitted their cart yet — their dishes won't be included in the order`;
  });
  readonly statusLabel = computed(() => {
    switch (this.status()) {
      case 'Open':
        return 'Collecting pre-orders';
      case 'Submitted':
        return 'Sent to Restaurant';
      case 'Closed':
        return 'Reservation closed';
    }
  });
  readonly canSubmit = computed(
    () =>
      this.status() === 'Open' &&
      this.consolidated().items.length > 0 &&
      !this.loading() &&
      !this.submitting() &&
      !this.syncing() &&
      !this.removing() &&
      !this.removeTarget() &&
      !this.loadError()
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    if (
      this.loading() ||
      this.submitting() ||
      this.syncing() ||
      this.removing() ||
      this.removeTarget()
    )
      return;
    if (!this.reservationId) {
      this.loadError.set('Group reservation not found');
      return;
    }
    this.loading.set(true);
    this.loadError.set(null);
    this.submitError.set(false);
    this.syncError.set(false);
    this.confirmationOpen.set(false);

    this.service
      .getGroupInvite$(this.reservationId)
      .pipe(
        switchMap((invite) =>
          forkJoin({
            sessions: this.service.getSessions$(invite.GroupReservationId),
            ownerSessionId: this.service.getOwnerSessionId$(this.reservationId),
            groupStatus: this.service.getStatus$(invite.GroupReservationId),
          }).pipe(map((result) => ({ ...result, invite })))
        ),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false))
      )
      .subscribe({
        next: ({ invite, sessions, ownerSessionId, groupStatus }) => {
          this.invite.set(invite);
          this.participants.set(sessions.participants);
          this.ownerSessionId.set(ownerSessionId);
          this.status.set(groupStatus.status);
          this.lastUpdated.set(new Date());
        },
        error: (error: unknown) => {
          this.loadError.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? 'Group reservation not found'
              : "We couldn't load this group pre-order. Please try again."
          );
        },
      });
  }

  refresh(): void {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId) return;
    this.refreshSnapshot$(groupId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }

  private refreshSnapshot$(groupId: string): Observable<void> {
    return defer(() => {
      if (
        this.syncing() ||
        this.loading() ||
        this.submitting() ||
        this.confirmationOpen() ||
        this.removeTarget() ||
        this.removing() ||
        this.status() !== 'Open'
      )
        return EMPTY;
      this.syncing.set(true);

      return forkJoin({
        sessions: this.service.getSessions$(groupId, { silent: true }),
        groupStatus: this.service.getStatus$(groupId, { silent: true }),
      }).pipe(
        tap(({ sessions, groupStatus }) => {
          this.participants.set(sessions.participants);
          this.status.set(groupStatus.status);
          this.lastUpdated.set(new Date());
          this.syncError.set(false);
        }),
        map(() => undefined),
        catchError(() => {
          this.syncError.set(true);
          return EMPTY;
        }),
        finalize(() => this.syncing.set(false))
      );
    });
  }

  readonly canRemove = computed(
    () =>
      this.status() === 'Open' &&
      !this.loading() &&
      !this.loadError() &&
      !this.syncing() &&
      !this.submitting() &&
      !this.confirmationOpen() &&
      !this.removing() &&
      !this.removeTarget()
  );

  openRemoveConfirmation(person: Participant): void {
    if (
      !this.canRemove() ||
      person.type !== 'Guest' ||
      person.id === this.ownerSessionId() ||
      !this.participants().some((participant) => participant.id === person.id)
    )
      return;
    this.removeError.set(false);
    this.removeTarget.set(person);
  }

  closeRemoveConfirmation(): void {
    if (!this.removing()) this.removeTarget.set(null);
  }

  removeGuest(): void {
    const target = this.removeTarget();
    const groupId = this.invite()?.GroupReservationId;
    if (
      !target ||
      !groupId ||
      this.removing() ||
      this.syncing() ||
      this.submitting() ||
      this.status() !== 'Open'
    )
      return;
    this.removing.set(true);
    this.removeError.set(false);
    this.service
      .removeSession$(groupId, target.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.removing.set(false))
      )
      .subscribe({
        next: (sessions) => {
          this.participants.set(sessions.participants);
          this.lastUpdated.set(new Date());
          this.syncError.set(false);
          this.removeTarget.set(null);
          this.toast.success('Guest removed from the reservation');
        },
        error: () => {
          // tokenInterceptor displays the API error toast. Keep the dialog for retry.
          this.removeError.set(true);
        },
      });
  }

  openConfirmation(): void {
    if (this.canSubmit()) this.confirmationOpen.set(true);
  }

  closeConfirmation(): void {
    if (!this.submitting()) this.confirmationOpen.set(false);
  }

  submit(): void {
    const invite = this.invite();
    if (!invite || !this.confirmationOpen() || !this.canSubmit()) return;
    this.submitting.set(true);
    this.submitError.set(false);

    this.service
      .submit$(invite.GroupReservationId, this.reservationId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false))
      )
      .subscribe({
        next: () => {
          this.status.set('Submitted');
          this.confirmationOpen.set(false);
          this.toast.success('Pre-order sent to the restaurant');
        },
        error: () => {
          // tokenInterceptor already shows the API error toast.
          this.submitError.set(true);
          this.confirmationOpen.set(false);
        },
      });
  }
}
