import { describe, expect, it } from "vitest";
import { validShiftStart } from "./shift";

describe("validShiftStart", () => {
  const now = new Date("2026-10-08T14:00:00Z");

  it("keeps a start from earlier in the shift", () => {
    expect(validShiftStart("2026-10-08T06:00:00.000Z", now)).toBe("2026-10-08T06:00:00.000Z");
  });

  it("drops a start that is too old to be this shift", () => {
    expect(validShiftStart("2026-10-07T20:00:00.000Z", now)).toBeNull();
  });

  it("drops missing, unreadable or future starts", () => {
    expect(validShiftStart(null, now)).toBeNull();
    expect(validShiftStart("not a date", now)).toBeNull();
    expect(validShiftStart("2026-10-08T15:00:00.000Z", now)).toBeNull();
  });
});
