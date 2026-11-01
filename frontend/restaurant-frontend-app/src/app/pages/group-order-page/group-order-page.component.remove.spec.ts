// Бонус · Remove Guest — сторінка. Запуск: npm run test:hw
import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError, Subject } from 'rxjs';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrderPageComponent } from './group-order-page.component';

const caesar = { id: 'dish1', name: 'Caesar Salad', price: 180 };
const SESSIONS: GroupSessionsResponse = {
  participants: [
    {
      id: 'owner',
      type: 'Owner',
      name: 'Yevhenii',
      isSubmitted: true,
      total: 180,
      items: [{ dish: caesar, quantity: 1 }],
    },
    {
      id: 'andrii',
      type: 'Guest',
      name: 'Andrii',
      isSubmitted: true,
      total: 360,
      items: [{ dish: caesar, quantity: 2 }],
    },
    {
      id: 'olena',
      type: 'Guest',
      name: 'Olena',
      isSubmitted: false,
      total: 180,
      items: [{ dish: caesar, quantity: 1 }],
    },
  ],
  total: 720,
};
const INVITE = {
  GroupReservationId: 'grp-1',
  LocationAddress: 'Rustaveli Avenue 48',
  MaxGuestCount: 5,
  StartTime: '2026-10-21T16:00:00Z',
  EndTime: '2026-10-21T17:30:00Z',
} as GuestInvite;

function buttonByText(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as
    | HTMLButtonElement
    | undefined;
}

describe('Бонус · Remove Guest', () => {
  let service: jasmine.SpyObj<GroupOrdersService>;

  function create() {
    TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent],
      providers: [
        { provide: GroupOrdersService, useValue: service },
        {
          provide: ToastService,
          useValue: jasmine.createSpyObj('ToastService', ['success', 'error', 'info']),
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ reservationId: 'res-1' }) } },
        },
      ],
    });
    const fixture = TestBed.createComponent(GroupOrderPageComponent);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function removeButtons(element: HTMLElement): HTMLButtonElement[] {
    return [...element.querySelectorAll('button')].filter(
      (b) => b.textContent?.trim() === 'Remove Guest'
    ) as HTMLButtonElement[];
  }

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', [
      'getGroupInvite$',
      'getSessions$',
      'getOwnerSessionId$',
      'getStatus$',
      'submit$',
      'removeSession$',
    ]);
    service.getGroupInvite$.and.returnValue(of(INVITE));
    service.getSessions$.and.returnValue(of(structuredClone(SESSIONS)));
    service.getOwnerSessionId$.and.returnValue(of('owner'));
    service.getStatus$.and.returnValue(
      of({ currentParticipants: 3, maxCapacity: 5, status: 'Open' as const })
    );
  });

  it('«Remove Guest» є біля кожного гостя і немає біля Owner', () => {
    expect(removeButtons(create().element).length).toBe(2);
  });

  it("кнопка має aria-label з ім'ям: «Remove Andrii»", () => {
    expect(removeButtons(create().element)[0].getAttribute('aria-label')).toBe('Remove Andrii');
  });

  it("клік → діалог з ім'ям гостя → підтвердження видаляє і оновлює список з відповіді", () => {
    service.removeSession$.and.returnValue(
      of({ participants: SESSIONS.participants.slice(0, 2), total: 540 })
    );
    const { fixture, element } = create();

    removeButtons(element)[1].click(); // Olena
    fixture.detectChanges();
    const dialog = element.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('Are you sure you want to remove');
    expect(dialog.textContent).toContain('Olena');

    buttonByText(dialog, 'Remove Guest')?.click();
    fixture.detectChanges();

    expect(service.removeSession$).toHaveBeenCalledOnceWith('grp-1', 'olena');
    expect(element.querySelectorAll('app-participant-card').length).toBe(2);
  });

  it('помилка DELETE: список лишається без змін', () => {
    service.removeSession$.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 400 }))
    );
    const { fixture, element } = create();
    removeButtons(element)[1].click();
    fixture.detectChanges();
    buttonByText(element.querySelector('[role="dialog"]')!, 'Remove Guest')?.click();
    fixture.detectChanges();
    expect(element.querySelectorAll('app-participant-card').length).toBe(3);
  });

  it('після Submit кнопок «Remove Guest» немає', () => {
    service.getStatus$.and.returnValue(
      of({ currentParticipants: 3, maxCapacity: 5, status: 'Submitted' as const })
    );
    expect(removeButtons(create().element).length).toBe(0);
  });
  it('Cancel closes the dialog without deleting or changing participants', () => {
    const { fixture, element } = create();
    removeButtons(element)[0].click();
    fixture.detectChanges();
    buttonByText(element.querySelector('[role="dialog"]')!, 'Cancel')!.click();
    fixture.detectChanges();
    expect(service.removeSession$).not.toHaveBeenCalled();
    expect(element.querySelector('[role="dialog"]')).toBeNull();
    expect(fixture.componentInstance.participants().length).toBe(3);
  });

  it('blocks polling, duplicate deletion and submit while removal is pending', fakeAsync(() => {
    const pending = new Subject<GroupSessionsResponse>();
    service.removeSession$.and.returnValue(pending);
    const { fixture } = create();
    const component = fixture.componentInstance;
    component.openRemoveConfirmation(SESSIONS.participants[1]);
    tick(15_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(1);
    component.removeGuest();
    component.removeGuest();
    component.closeRemoveConfirmation();
    component.refresh();
    component.openConfirmation();
    tick(30_000);
    expect(service.removeSession$).toHaveBeenCalledTimes(1);
    expect(service.getSessions$).toHaveBeenCalledTimes(1);
    expect(component.removeTarget()?.id).toBe('andrii');
    expect(component.confirmationOpen()).toBeFalse();
    pending.next({ participants: [SESSIONS.participants[0]], total: 180 });
    pending.complete();
    fixture.detectChanges();
    tick(15_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(2);
    fixture.destroy();
    discardPeriodicTasks();
  }));

  it('allows retry after a failed deletion without showing a duplicate error toast', () => {
    service.removeSession$.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 }))
    );
    const { fixture } = create();
    const component = fixture.componentInstance;
    component.openRemoveConfirmation(SESSIONS.participants[1]);
    component.removeGuest();
    expect(component.removeError()).toBeTrue();
    expect(component.removing()).toBeFalse();
    expect(component.participants().length).toBe(3);
    expect(TestBed.inject(ToastService).error).not.toHaveBeenCalled();
    service.removeSession$.and.returnValue(
      of({ participants: [SESSIONS.participants[0]], total: 180 })
    );
    component.removeGuest();
    expect(component.removeTarget()).toBeNull();
    expect(component.participants().length).toBe(1);
  });

  it('recomputes submitted totals from the server response and never removes the owner', () => {
    const { fixture } = create();
    const component = fixture.componentInstance;
    component.openRemoveConfirmation(SESSIONS.participants[0]);
    expect(component.removeTarget()).toBeNull();
    service.removeSession$.and.returnValue(
      of({
        participants: [SESSIONS.participants[0], SESSIONS.participants[2]],
        total: 360,
      })
    );
    component.openRemoveConfirmation(SESSIONS.participants[1]);
    component.removeGuest();
    expect(component.grandTotal()).toBe(180);
    expect(component.pendingCount()).toBe(1);
    expect(component.participantCount()).toBe(2);
  });
});
