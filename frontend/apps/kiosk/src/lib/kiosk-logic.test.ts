import { describe, expect, it } from "vitest";
import { cartTotal, makeLine, setQuantity, toOrderLines } from "./cart";
import { initialSelection, isGroupValid, pickHint, toggle, unitPrice, type ModifierGroup, type Product } from "./customize";

const group = (id: string, min: number, max: number, required: boolean, options: [string, number, boolean?][]): ModifierGroup => ({
  id,
  name: id,
  minSelect: min,
  maxSelect: max,
  isRequired: required,
  modifiers: options.map(([name, priceDelta, isAvailable = true]) => ({ id: `${id}:${name}`, name, priceDelta, isAvailable })),
});

const drink = group("drink", 1, 1, true, [["Coke", 0], ["Pineapple", 10]]);
const upsizeFries = group("fries", 1, 1, true, [["Regular", 0], ["Large", 25]]);
const addOns = group("addons", 0, 2, false, [["Gravy", 15], ["Rice", 25], ["Sundae", 45]]);
const addDrink = group("adddrink", 0, 1, false, [["Coke", 45]]);

const meal: Product = {
  id: "meal",
  name: "1-pc Chicken Meal",
  description: null,
  price: 189,
  isSoldOut: false,
  media: [],
  modifierGroups: [drink, upsizeFries, addOns, addDrink],
};

describe("customization rules", () => {
  it("pre-answers required single-choice questions whose first option is free", () => {
    const s = initialSelection(meal);
    expect(s.drink).toEqual(["drink:Coke"]);
    expect(s.fries).toEqual(["fries:Regular"]);
    expect(s.addons).toEqual([]);
    expect(s.adddrink).toEqual([]);
  });

  it("does not pre-answer when the first option costs extra or is unavailable", () => {
    const pricey = group("x", 1, 1, true, [["Large", 20]]);
    const soldOut = group("y", 1, 1, true, [["Regular", 0, false], ["Large", 20]]);
    const s = initialSelection({ ...meal, modifierGroups: [pricey, soldOut] });
    expect(s.x).toEqual([]);
    expect(s.y).toEqual([]);
  });

  it("swaps single-choice picks and caps multi-choice picks at max", () => {
    let s = initialSelection(meal);
    s = toggle(s, upsizeFries, "fries:Large");
    expect(s.fries).toEqual(["fries:Large"]);

    s = toggle(s, addOns, "addons:Gravy");
    s = toggle(s, addOns, "addons:Rice");
    s = toggle(s, addOns, "addons:Sundae"); // over max 2: ignored
    expect(s.addons).toEqual(["addons:Gravy", "addons:Rice"]);

    s = toggle(s, addOns, "addons:Gravy");
    expect(s.addons).toEqual(["addons:Rice"]);
  });

  it("lets an optional single choice be un-picked, but not a required one", () => {
    let s = toggle(initialSelection(meal), addDrink, "adddrink:Coke");
    expect(s.adddrink).toEqual(["adddrink:Coke"]);
    s = toggle(s, addDrink, "adddrink:Coke");
    expect(s.adddrink).toEqual([]);
    expect(toggle(s, drink, "drink:Coke").drink).toEqual(["drink:Coke"]);
  });

  it("validates min and max", () => {
    expect(isGroupValid(drink, { drink: [] })).toBe(false);
    expect(isGroupValid(drink, { drink: ["drink:Coke"] })).toBe(true);
    expect(isGroupValid(addOns, { addons: [] })).toBe(true);
  });

  it("prices the unit as base plus modifier deltas", () => {
    let s = initialSelection(meal);
    s = toggle(s, upsizeFries, "fries:Large");
    s = toggle(s, addOns, "addons:Gravy");
    expect(unitPrice(meal, s)).toBe(229);
  });

  it("describes how many to pick", () => {
    expect(pickHint(drink)).toBe("Pick 1");
    expect(pickHint(addOns)).toBe("Optional — pick up to 2");
    expect(pickHint(addDrink)).toBe("Optional");
  });
});

describe("cart", () => {
  it("totals lines, removes at zero, and sends only ids and quantities", () => {
    const s = toggle(initialSelection(meal), upsizeFries, "fries:Large");
    const a = makeLine(meal, s, 2);
    const b = makeLine({ ...meal, id: "pie", name: "Apple Pie", price: 45, modifierGroups: [] }, {}, 1);

    expect(cartTotal([a, b])).toBe(2 * 214 + 45);
    expect(setQuantity([a, b], a.key, 0)).toEqual([b]);
    expect(toOrderLines([a])).toEqual([
      { productId: "meal", quantity: 2, modifierIds: ["drink:Coke", "fries:Large"], notes: null },
    ]);
    expect(JSON.stringify(toOrderLines([a]))).not.toContain("price");
  });
});
