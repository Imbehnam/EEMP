import { describe, expect, it } from 'vitest';

import { instantDayIso, weekDays, weekStartIso, zonedToUtc, zoneOffsetMs } from '../src';

describe('zoned scheduling helpers', () => {
  it('converts Tehran wall-clock times (UTC+03:30) to UTC instants and back to days', () => {
    expect(zoneOffsetMs(Date.UTC(2026, 11, 5, 6, 30), 'Asia/Tehran')).toBe(3.5 * 3_600_000);
    expect(zonedToUtc('2026-12-05', '10:00', 'Asia/Tehran')).toBe('2026-12-05T06:30:00.000Z');
    expect(zonedToUtc('2026-12-05', '01:00', 'Asia/Tehran')).toBe('2026-12-04T21:30:00.000Z');
    expect(instantDayIso('2026-12-04T21:30:00Z', 'Asia/Tehran')).toBe('2026-12-05');
    expect(instantDayIso('2026-12-04T21:30:00Z', 'UTC')).toBe('2026-12-04');
  });

  it('handles daylight-saving zones', () => {
    // Istanbul has no DST (UTC+3); New York switches on 2026-03-08.
    expect(zonedToUtc('2026-06-01', '09:00', 'Europe/Istanbul')).toBe('2026-06-01T06:00:00.000Z');
    expect(zonedToUtc('2026-03-07', '09:00', 'America/New_York')).toBe('2026-03-07T14:00:00.000Z');
    expect(zonedToUtc('2026-03-09', '09:00', 'America/New_York')).toBe('2026-03-09T13:00:00.000Z');
  });

  it('rejects invalid input', () => {
    expect(zonedToUtc('2026-13-01', '10:00', 'UTC')).toBeNull();
    expect(zonedToUtc('2026-12-01', '25:00', 'UTC')).toBeNull();
    expect(zonedToUtc('2026-12-01', 'x', 'UTC')).toBeNull();
  });

  it('builds Saturday-first weeks', () => {
    // 2026-12-02 is a Wednesday; its week starts on Saturday 2026-11-28.
    expect(weekStartIso('2026-12-02')).toBe('2026-11-28');
    expect(weekStartIso('2026-11-28')).toBe('2026-11-28');
    expect(weekStartIso('2026-12-04')).toBe('2026-11-28');
    expect(weekDays('2026-11-28')).toEqual(['2026-11-28', '2026-11-29', '2026-11-30', '2026-12-01', '2026-12-02', '2026-12-03', '2026-12-04']);
  });
});
