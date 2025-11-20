import { TestBed } from '@angular/core/testing';
import { IdempotencyService } from './idempotency.service';

describe('IdempotencyService', () => {
  let service: IdempotencyService;
  let store: Record<string,string>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [IdempotencyService] });
    service = TestBed.inject(IdempotencyService);
    store = {};
    spyOn(globalThis.localStorage, 'getItem').and.callFake((k: string) => store[k] ?? null);
    spyOn(globalThis.localStorage, 'setItem').and.callFake((k: string, v: string) => { store[k] = v; });
    spyOn(globalThis.localStorage, 'removeItem').and.callFake((k: string) => { delete store[k]; });
  });

  it('returns null when missing identifiers', () => {
    expect(service.load(null, 'F1')).toBeNull();
    expect(service.load('J1', null)).toBeNull();
  });

  it('persists and loads key correctly', () => {
    service.persist('J100', 'F200', 'abc123');
    expect(store['tsic:payidem:J100:F200']).toBe('abc123');
    const val = service.load('J100', 'F200');
    expect(val).toBe('abc123');
  });

  it('clears key', () => {
    service.persist('J100', 'F200', 'to-clear');
    expect(store['tsic:payidem:J100:F200']).toBe('to-clear');
    service.clear('J100', 'F200');
    expect(store['tsic:payidem:J100:F200']).toBeUndefined();
    expect(service.load('J100', 'F200')).toBeNull();
  });

  it('does not throw on storage errors', () => {
    (globalThis.localStorage.setItem as jasmine.Spy).and.callFake(() => { throw new Error('fail'); });
    (globalThis.localStorage.getItem as jasmine.Spy).and.callFake(() => { throw new Error('fail'); });
    (globalThis.localStorage.removeItem as jasmine.Spy).and.callFake(() => { throw new Error('fail'); });
    expect(() => service.persist('J1','F1','v')).not.toThrow();
    expect(() => service.load('J1','F1')).not.toThrow();
    expect(service.load('J1','F1')).toBeNull();
    expect(() => service.clear('J1','F1')).not.toThrow();
  });
});
