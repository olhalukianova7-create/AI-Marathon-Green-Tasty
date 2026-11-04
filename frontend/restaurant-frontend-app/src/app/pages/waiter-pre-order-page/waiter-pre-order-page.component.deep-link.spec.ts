import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import { WaiterPreOrderPageComponent } from './waiter-pre-order-page.component';

describe('Waiter pre-order deep link', () => {
  afterEach(() => sessionStorage.removeItem('waiterPreOrderData'));

  it('loads the linked reservation instead of stale navigation storage', async () => {
    sessionStorage.setItem('waiterPreOrderData', JSON.stringify({
      reservationId: 'stale', createdByName: 'Stale', createdByRole: 'Customer', tableName: '1',
    }));
    const service = jasmine.createSpyObj<PreOrdersService>('PreOrdersService', ['getByReservationId']);
    service.getByReservationId.and.returnValue(of({
      orderId: 'order', reservationId: 'linked', reservationAddress: 'Rustaveli Avenue 48',
      reservationTime: 'Tomorrow', status: 3, orderItems: [],
    }));
    TestBed.configureTestingModule({
      imports: [WaiterPreOrderPageComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ reservationId: 'linked' }) } } },
        { provide: PreOrdersService, useValue: service },
      ],
    });
    const fixture = TestBed.createComponent(WaiterPreOrderPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(service.getByReservationId).toHaveBeenCalledOnceWith('linked');
    expect(fixture.componentInstance.reservationId).toBe('linked');
    expect((fixture.nativeElement as HTMLElement).querySelector('h2')?.textContent).toContain('Rustaveli Avenue 48');
  });
});
