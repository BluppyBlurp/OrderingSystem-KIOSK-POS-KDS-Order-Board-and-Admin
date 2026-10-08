import { describe, expect, it } from "vitest";
import { parseAmount, pressKey, quickAmounts } from "./cash";

describe("quickAmounts", () => {
  it("offers exact, then the next round bills", () => {
    expect(quickAmounts(285)).toEqual([285, 300, 500, 1000]);
    expect(quickAmounts(767)).toEqual([767, 800, 1000]);
  });

  it("does not repeat the exact amount when it is already round", () => {
    expect(quickAmounts(500)).toEqual([500, 1000]);
  });

  it("keeps centavos on the exact amount", () => {
    expect(quickAmounts(149.5)[0]).toBe(149.5);
  });
});

describe("pressKey", () => {
  const type = (...keys: string[]) => keys.reduce(pressKey, "");

  it("builds an amount from digits", () => {
    expect(type("5", "0", "0")).toBe("500");
    expect(type("1", "00")).toBe("100");
  });

  it("ignores leading zeros", () => {
    expect(type("0", "0", "7")).toBe("7");
  });

  it("allows one decimal point and two centavo digits", () => {
    expect(type(".", "5")).toBe("0.5");
    expect(type("1", ".", "2", "5", "9")).toBe("1.25");
    expect(type("1", ".", ".")).toBe("1.");
  });

  it("deletes and clears", () => {
    expect(type("1", "2", "⌫")).toBe("1");
    expect(type("1", "2", "C")).toBe("");
  });

  it("caps very large amounts", () => {
    expect(type("1", "2", "3", "4", "5", "6", "7", "8")).toBe("1234567");
  });
});

describe("parseAmount", () => {
  it("reads typed amounts", () => {
    expect(parseAmount("")).toBe(0);
    expect(parseAmount("500")).toBe(500);
    expect(parseAmount("12.5")).toBe(12.5);
  });
});
