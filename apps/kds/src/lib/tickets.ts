/** Ticket age, colour bands and column grouping for the kitchen screen. */

export type AgeLevel = "ok" | "warn" | "late";

/** Minutes since payment after which a ticket turns amber, then red. */
export const WARN_MINUTES = 5;
export const LATE_MINUTES = 10;

export function ageSeconds(paidAt: string | null, createdAt: string, now: number): number {
  return Math.max(0, Math.floor((now - new Date(paidAt ?? createdAt).getTime()) / 1000));
}

export function ageLevel(seconds: number): AgeLevel {
  if (seconds >= LATE_MINUTES * 60) return "late";
  if (seconds >= WARN_MINUTES * 60) return "warn";
  return "ok";
}

/** "0:07", "4:59", "12:03", "1:02:00". */
export function formatAge(seconds: number): string {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = String(seconds % 60).padStart(2, "0");
  return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${s}` : `${m}:${s}`;
}

/** Ids present now that were not in the previous list: tickets that just arrived. */
export function newIds(previous: Set<string> | null, current: string[]): string[] {
  if (previous === null) return []; // first load: nothing is "new"
  return current.filter((id) => !previous.has(id));
}
