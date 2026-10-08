/**
 * When the current cashier's shift started, remembered on this till so the summary survives a page reload.
 * Without a saved start the API counts from the start of today's business date.
 */

/** A shift longer than this is a start someone forgot to reset (e.g. yesterday's); fall back to "today". */
export const MAX_SHIFT_HOURS = 16;

const key = (cashier: string) => `pos.shiftStart:${cashier}`;

/** The saved start if it is still plausible at `now`, else null. */
export function validShiftStart(saved: string | null, now: Date): string | null {
  if (!saved) return null;
  const started = new Date(saved).getTime();
  if (Number.isNaN(started) || started > now.getTime()) return null;
  return now.getTime() - started <= MAX_SHIFT_HOURS * 3_600_000 ? saved : null;
}

export function readShiftStart(cashier: string, now = new Date()): string | null {
  try {
    return validShiftStart(localStorage.getItem(key(cashier)), now);
  } catch {
    return null;
  }
}

export function saveShiftStart(cashier: string, start: Date): string {
  const iso = start.toISOString();
  try {
    localStorage.setItem(key(cashier), iso);
  } catch {
    /* storage blocked: the shift start lasts until reload */
  }
  return iso;
}
