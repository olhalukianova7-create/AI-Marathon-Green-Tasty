import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AuthenticationService } from '@app/auth/services/authentication.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReportingDashboardComponent } from './reporting-dashboard.component';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

describe('ReportingDashboardComponent', () => {
  let component: ReportingDashboardComponent;
  let fixture: ComponentFixture<ReportingDashboardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: 'https://test.api' },
        {
          provide: AuthenticationService,
          useValue: {
            getToken: () => undefined,
            getUser: () => null,
            user$: of(null),
            isAuthorised$: of(false),
          },
        }],
      imports: [ReportingDashboardComponent],
    }).compileComponents();

    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    fixture = TestBed.createComponent(ReportingDashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
