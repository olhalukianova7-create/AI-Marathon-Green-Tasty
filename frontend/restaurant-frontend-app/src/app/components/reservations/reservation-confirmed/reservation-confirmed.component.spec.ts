import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ReservationConfirmedComponent } from './reservation-confirmed.component';

describe('ReservationConfirmedComponent', () => {
  let component: ReservationConfirmedComponent;
  let fixture: ComponentFixture<ReservationConfirmedComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReservationConfirmedComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ReservationConfirmedComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('reservation', { id: 'reservation-1', tableId: 'table-1', customerId: 'customer-1', waiterId: 'waiter-1', startTime: new Date('2026-11-08T16:00:00Z'), endTime: new Date('2026-11-08T17:30:00Z'), guestCount: 3, status: 0, locationAddress: 'Rustaveli Avenue 48', restaurantName: 'Green & Tasty', tableCapacity: 6 });
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
