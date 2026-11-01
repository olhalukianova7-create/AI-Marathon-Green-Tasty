import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ReservationCustomer, ReservationStatusCustomer } from '@app/models/reservations/reservation.model';
import { Roles } from '@app/shared/constants/roles';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { CardReservationComponent } from './card-reservation.component';

const RESERVATION: ReservationCustomer = {
  id: 'group-reservation-1', tableId: 'table-5', guestCount: 3,
  startTime: new Date('2026-11-08T16:00:00Z'), endTime: new Date('2026-11-08T17:30:00Z'),
  status: ReservationStatusCustomer.Reserved, restaurantName: 'Green & Tasty',
  locationAddress: 'Rustaveli Avenue 48', waiterId: 'waiter-1',
};

describe('Reservation card group pre-order navigation', () => {
  let fixture: ComponentFixture<CardReservationComponent>;
  let navigate: jasmine.Spy;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CardReservationComponent],
      providers: [provideRouter([]), { provide: API_BASE_URL, useValue: 'https://test.api' }],
    }).compileComponents();
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(CardReservationComponent);
    fixture.componentRef.setInput('r', RESERVATION);
    fixture.detectChanges();
  });

  function viewButton(): HTMLButtonElement | undefined {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find((button) => button.textContent?.trim() === 'View Pre-order');
  }

  it('opens the group overview for this reservation', () => {
    expect(viewButton()).toBeDefined();
    viewButton()!.click();
    expect(navigate).toHaveBeenCalledOnceWith(['/reservations', RESERVATION.id, 'group-order']);
  });

  it('remains available after the customer submits a pre-order', () => {
    fixture.componentRef.setInput('r', { ...RESERVATION, hasSubmittedPreOrder: true });
    fixture.detectChanges();
    expect(viewButton()).toBeDefined();
  });

  it('allows reviewing a completed group reservation', () => {
    fixture.componentRef.setInput('r', { ...RESERVATION, status: ReservationStatusCustomer.Finished });
    fixture.detectChanges();
    expect(viewButton()).toBeDefined();
  });

  it('does not offer the owner overview to a waiter', () => {
    fixture.componentRef.setInput('role', Roles.WAITER);
    fixture.detectChanges();
    expect(viewButton()).toBeUndefined();
    fixture.componentInstance.viewPreOrder();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not offer the group overview for a single guest', () => {
    fixture.componentRef.setInput('r', { ...RESERVATION, guestCount: 1 });
    fixture.detectChanges();
    expect(viewButton()).toBeUndefined();
    fixture.componentInstance.viewPreOrder();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not offer the group overview for a cancelled reservation', () => {
    fixture.componentRef.setInput('r', { ...RESERVATION, status: ReservationStatusCustomer.Cancelled });
    fixture.detectChanges();
    expect(viewButton()).toBeUndefined();
    fixture.componentInstance.viewPreOrder();
    expect(navigate).not.toHaveBeenCalled();
  });
});
