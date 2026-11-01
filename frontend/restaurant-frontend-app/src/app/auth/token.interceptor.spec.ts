import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthenticationService } from './services/authentication.service';
import { ToastService } from '@app/services/toast/toast.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { tokenInterceptor } from './token.interceptor';

describe('Owner and guest HTTP authorization', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let auth: jasmine.SpyObj<AuthenticationService>;
  let toast: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    auth = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', ['getTokens']);
    auth.getTokens.and.returnValue({ idToken: 'owner-token', accessToken: 'access-token' });
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['error', 'success', 'info']);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthenticationService, useValue: auth },
        { provide: ToastService, useValue: toast },
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('authenticates the owner when fetching group sessions', () => {
    client.get('/guest/groups/group/sessions').subscribe();
    const request = http.expectOne('/guest/groups/group/sessions');
    expect(request.request.headers.get('Authorization')).toBe('Bearer owner-token');
    request.flush({ participants: [], total: 0 });
  });

  it('keeps the guest cart token and body unchanged', () => {
    const body = { dishId: 'dish', quantity: 2 };
    client
      .post('/guest/sessions/session/dishes', body, {
        headers: { Authorization: 'Bearer guest-token' },
      })
      .subscribe();
    const request = http.expectOne('/guest/sessions/session/dishes');
    expect(request.request.headers.get('Authorization')).toBe('Bearer guest-token');
    expect(request.request.body).toEqual(body);
    expect(auth.getTokens).not.toHaveBeenCalled();
    request.flush({});
  });

  for (const endpoint of ['/guest/groups/group/sessions', '/guest/groups/group/status']) {
    it(`keeps background failures silent for ${endpoint}`, () => {
      let status = 0;
      client
        .get(endpoint, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
        .subscribe({ error: (error: { status: number }) => (status = error.status) });
      http
        .expectOne(endpoint)
        .flush({ error: 'Temporarily unavailable' }, { status: 500, statusText: 'Server Error' });
      expect(status).toBe(500);
      expect(toast.error).not.toHaveBeenCalled();
    });
  }

  it('shows the server message once for a failed group submission', () => {
    client
      .post('/group-reservations/group/submit', { reservationId: 'reservation' })
      .subscribe({ error: () => undefined });
    const request = http.expectOne('/group-reservations/group/submit');
    expect(request.request.headers.get('Authorization')).toBe('Bearer owner-token');
    request.flush({ error: 'Order was not found' }, { status: 400, statusText: 'Bad Request' });
    expect(toast.error).toHaveBeenCalledOnceWith('Order was not found', 'Error');
  });
});
