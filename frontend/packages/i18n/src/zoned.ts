import { addDaysIso, isoWeekdayIndex, parseIsoDate, todayIso } from './calendar';

/**
 * Wall-clock helpers for scheduling (EEMP-012): the API stores UTC instants, people pick a day and a time in their own time zone.
 * Uses Intl only (no tz database bundled), so any IANA zone the browser knows works, including DST transitions.
 */

/** Offset (ms) of `timeZone` at the UTC instant `utcMs`: local wall clock minus UTC. */
export function zoneOffsetMs(utcMs: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone, hourCycle: 'h23', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).formatToParts(new Date(utcMs));
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? '0');
  const asUtc = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour') % 24, get('minute'), get('second'));
  return asUtc - Math.floor(utcMs / 1000) * 1000;
}

/** UTC instant (ISO string) of the wall-clock `isoDate` + `time` ("HH:mm") in `timeZone`, or null for invalid input. */
export function zonedToUtc(isoDate: string, time: string, timeZone: string): string | null {
  const d = parseIsoDate(isoDate);
  const m = /^(\d{1,2}):(\d{2})$/.exec(time.trim());
  if (!d || !m) return null;
  const hours = Number(m[1]);
  const minutes = Number(m[2]);
  if (hours > 23 || minutes > 59) return null;
  const wall = Date.UTC(d.year, d.month - 1, d.day, hours, minutes);
  // Two passes settle the offset around DST changes.
  let utc = wall - zoneOffsetMs(wall, timeZone);
  utc = wall - zoneOffsetMs(utc, timeZone);
  return new Date(utc).toISOString();
}

/** Calendar day (yyyy-MM-dd, Gregorian) of an instant in `timeZone`. */
export const instantDayIso = (instant: string | number | Date, timeZone: string) =>
  todayIso(timeZone, instant instanceof Date ? instant : new Date(instant));

/** Saturday that starts the week (Iranian week) containing `isoDate`. */
export function weekStartIso(isoDate: string): string {
  const i = isoWeekdayIndex(isoDate);
  return addDaysIso(isoDate, -((i + 1) % 7)) ?? isoDate;
}

/** The seven calendar days of the Saturday-first week starting at `startIso`. */
export const weekDays = (startIso: string): string[] => Array.from({ length: 7 }, (_, i) => addDaysIso(startIso, i) ?? startIso);
