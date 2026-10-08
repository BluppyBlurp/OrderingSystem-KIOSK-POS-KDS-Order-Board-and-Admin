import { describe, expect, it } from "vitest";
import { ageLevel, ageSeconds, formatAge, newIds } from "./tickets";

describe("ticket age", () => {
  const paid = "2026-10-08T12:00:00Z";
  const at = (iso: string) => new Date(iso).getTime();

  it("counts from payment, falling back to creation", () => {
    expect(ageSeconds(paid, "2026-10-08T11:50:00Z", at("2026-10-08T12:01:30Z"))).toBe(90);
    expect(ageSeconds(null, "2026-10-08T11:59:00Z", at("2026-10-08T12:00:00Z"))).toBe(60);
    expect(ageSeconds(paid, paid, at("2026-10-08T11:59:00Z"))).toBe(0); // clock skew never goes negative
  });

  it("turns amber at 5 minutes and red at 10", () => {
    expect(ageLevel(299)).toBe("ok");
    expect(ageLevel(300)).toBe("warn");
    expect(ageLevel(600)).toBe("late");
  });

  it("formats minutes and hours", () => {
    expect(formatAge(7)).toBe("0:07");
    expect(formatAge(723)).toBe("12:03");
    expect(formatAge(3720)).toBe("1:02:00");
  });
});

describe("newIds", () => {
  it("reports nothing on the first load, then only arrivals", () => {
    expect(newIds(null, ["a", "b"])).toEqual([]);
    expect(newIds(new Set(["a"]), ["a", "b", "c"])).toEqual(["b", "c"]);
  });
});
