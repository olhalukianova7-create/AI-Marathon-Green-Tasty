import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TimeSlotsModalComponent } from './time-slots-modal.component';

describe('TimeSlotsModalComponent', () => {
  let component: TimeSlotsModalComponent;
  let fixture: ComponentFixture<TimeSlotsModalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TimeSlotsModalComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(TimeSlotsModalComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('table', { id: 'table-1', tableNumber: 5, locationId: 'location-1', capacity: 6, timeRanges: [{ start: '2026-11-08T16:00:00Z', end: '2026-11-08T17:30:00Z' }] });
    fixture.componentRef.setInput('date', '2026-11-08');
    fixture.componentRef.setInput('locationName', 'Rustaveli Avenue 48');
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
