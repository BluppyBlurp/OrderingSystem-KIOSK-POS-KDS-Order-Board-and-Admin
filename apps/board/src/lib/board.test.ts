import { describe, expect, it } from "vitest";
import type { BoardEntry } from "../api";
import { newlyReady, shortNumber } from "./board";

const entry = (orderNumber: string): BoardEntry => ({ orderNumber, diningOption: "DineIn", type: "CounterPickup", tableNumber: null });

describe("newlyReady", () => {
  it("stays quiet on the first load", () => {
    expect(newlyReady(null, [entry("A-101")])).toEqual([]);
  });

  it("reports only numbers that just moved to Now serving", () => {
    expect(newlyReady(new Set(["A-101"]), [entry("A-101"), entry("A-102")])).toEqual(["A-102"]);
  });
});

describe("shortNumber", () => {
  it("drops the letter prefix", () => {
    expect(shortNumber("A-101")).toBe("101");
    expect(shortNumber("115")).toBe("115");
  });
});
