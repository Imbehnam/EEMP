import { describe, expect, it } from 'vitest';

import { isActivePath, switchLocalePath } from '../src/paths';

describe('switchLocalePath', () => {
  it('replaces or inserts the locale prefix and keeps the query', () => {
    expect(switchLocalePath('/fa/users/1', '?notice=saved', 'en')).toBe('/en/users/1?notice=saved');
    expect(switchLocalePath('/fa', '', 'ar')).toBe('/ar');
    expect(switchLocalePath('/', '', 'tr')).toBe('/tr');
  });
});

describe('isActivePath', () => {
  it('matches sections without matching the home link everywhere', () => {
    expect(isActivePath('/people/students/new', '/people/students')).toBe(true);
    expect(isActivePath('/users', '/')).toBe(false);
    expect(isActivePath('/', '/')).toBe(true);
  });
});
