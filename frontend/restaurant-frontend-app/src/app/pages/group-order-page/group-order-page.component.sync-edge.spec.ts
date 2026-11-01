import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { Subject, of } from 'rxjs';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrderPageComponent } from './group-order-page.component';

describe('Group synchronization lifecycle', () => {
  let service: jasmine.SpyObj<GroupOrdersService>;
  const sessions: GroupSessionsResponse = { participants: [], total: 0 };
  const invite = {
    GroupReservationId: 'group',
    LocationAddress: 'Address',
    MaxGuestCount: 5,
    StartTime: '2026-10-21T16:00:00Z',
    EndTime: '2026-10-21T17:30:00Z',
  } as GuestInvite;

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', [
      'getGroupInvite$',
      'getSessions$',
      'getOwnerSessionId$',
      'getStatus$',
      'submit$',
    ]);
    service.getGroupInvite$.and.returnValue(of(invite));
    service.getSessions$.and.returnValue(of(sessions));
    service.getOwnerSessionId$.and.returnValue(of('owner'));
    service.getStatus$.and.returnValue(
      of({ currentParticipants: 0, maxCapacity: 5, status: 'Open' })
    );
    TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent],
      providers: [
        { provide: GroupOrdersService, useValue: service },
        {
          provide: ToastService,
          useValue: jasmine.createSpyObj('ToastService', ['success', 'error']),
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ reservationId: 'reservation' }) } },
        },
      ],
    });
  });

  it('does not overlap automatic or manual refresh with an outstanding request', fakeAsync(() => {
    const fixture = TestBed.createComponent(GroupOrderPageComponent);
    fixture.detectChanges();
    const pending = new Subject<GroupSessionsResponse>();
    service.getSessions$.and.returnValue(pending);
    tick(15_000);
    fixture.componentInstance.refresh();
    tick(30_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(2);
    pending.next(sessions);
    pending.complete();
    service.getSessions$.and.returnValue(of(sessions));
    tick(15_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(3);
    fixture.destroy();
    discardPeriodicTasks();
  }));

  it('reloading does not create a second polling timer', fakeAsync(() => {
    const fixture = TestBed.createComponent(GroupOrderPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.load();
    fixture.detectChanges();
    tick(15_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(3);
    fixture.destroy();
    discardPeriodicTasks();
  }));

  it('stops polling when the server reports that the group was submitted', fakeAsync(() => {
    const fixture = TestBed.createComponent(GroupOrderPageComponent);
    fixture.detectChanges();
    service.getStatus$.and.returnValue(
      of({ currentParticipants: 0, maxCapacity: 5, status: 'Submitted' })
    );
    tick(15_000);
    fixture.detectChanges();
    tick(30_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(2);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Sent to Restaurant');
    fixture.destroy();
    discardPeriodicTasks();
  }));
});
