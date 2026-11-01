import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DishSelectCardComponent } from './dish-select-card.component';

describe('DishSelectCardComponent', () => {
  let component: DishSelectCardComponent;
  let fixture: ComponentFixture<DishSelectCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DishSelectCardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(DishSelectCardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('dish', { id: 'dish-1', name: 'Spring Salad', imageUrl: 'assets/img/dishes/spring-salad.jpg', price: 12, state: 'Available', weight: '430 g', dishType: 'Appetizer', description: 'Seasonal greens', calories: '390', proteins: '12', carbohydrates: '42', fats: '18', vitamins: 'A, C' });
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
