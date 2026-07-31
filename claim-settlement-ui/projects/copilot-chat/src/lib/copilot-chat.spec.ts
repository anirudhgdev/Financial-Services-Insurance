import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CopilotChat } from './copilot-chat';

describe('CopilotChat', () => {
  let component: CopilotChat;
  let fixture: ComponentFixture<CopilotChat>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CopilotChat]
    })
    .compileComponents();

    fixture = TestBed.createComponent(CopilotChat);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
