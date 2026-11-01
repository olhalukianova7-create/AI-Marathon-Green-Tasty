import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MakeReservationComponent } from './make-reservation.component';

describe('MakeReservationComponent', () => {
  let component: MakeReservationComponent;
  let fixture: ComponentFixture<MakeReservationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MakeReservationComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(MakeReservationComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('table', { id: 'table-1', tableNumber: 5, locationId: 'location-1', capacity: 6, timeRanges: [{ start: '2026-11-08T16:00:00Z', end: '2026-11-08T17:30:00Z' }] });
    fixture.componentRef.setInput('selectedTimeRange', { start: '2026-11-08T16:00:00Z', end: '2026-11-08T17:30:00Z' });
    fixture.componentRef.setInput('locationName', 'Rustaveli Avenue 48');
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
