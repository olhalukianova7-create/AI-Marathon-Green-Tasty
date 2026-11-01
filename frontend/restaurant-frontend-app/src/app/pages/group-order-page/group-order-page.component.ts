import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { finalize, forkJoin, map, switchMap } from 'rxjs';
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

  readonly participantCount = computed(() => this.participants().length);
  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  readonly grandTotal = computed(() => this.consolidated().total);
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
      !this.loadError()
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    if (this.loading() || this.submitting()) return;
    if (!this.reservationId) {
      this.loadError.set('Group reservation not found');
      return;
    }
    this.loading.set(true);
    this.loadError.set(null);
    this.submitError.set(false);
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
