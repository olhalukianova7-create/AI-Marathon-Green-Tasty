import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { GroupOrdersService } from './group-orders.service';

describe('GroupOrdersService API errors', () => {
  let service: GroupOrdersService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'https://api.test' },
      ],
    });
    service = TestBed.inject(GroupOrdersService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('preserves a missing reservation error without requesting an invite', () => {
    let status = 0;
    service
      .getGroupInvite$('missing')
      .subscribe({ error: (error: HttpErrorResponse) => (status = error.status) });
    http
      .expectOne('https://api.test/reservations/missing/invite-token')
      .flush({ error: 'Reservation not found' }, { status: 404, statusText: 'Not Found' });
    expect(status).toBe(404);
    http.expectNone((request) => request.url.includes('/guest/invites/'));
  });

  it('preserves a failed submission so the page can allow another attempt', () => {
    let serverMessage = '';
    service.submit$('group', 'reservation').subscribe({
      error: (error: HttpErrorResponse) => (serverMessage = error.error.error),
    });
    http
      .expectOne('https://api.test/group-reservations/group/submit')
      .flush({ error: 'Order was not found' }, { status: 400, statusText: 'Bad Request' });
    expect(serverMessage).toBe('Order was not found');
  });
});
