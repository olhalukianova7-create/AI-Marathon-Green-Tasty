import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DishModalComponent } from './dish-modal.component';

describe('DishModalComponent', () => {
  let component: DishModalComponent;
  let fixture: ComponentFixture<DishModalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DishModalComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(DishModalComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('dish', { id: 'dish-1', name: 'Spring Salad', imageUrl: 'assets/img/dishes/spring-salad.jpg', price: 12, state: 'Available', weight: '430 g', dishType: 'Appetizer', description: 'Seasonal greens', calories: '390', proteins: '12', carbohydrates: '42', fats: '18', vitamins: 'A, C' });
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
