import { afterEach, describe, expect, it, vi } from "vitest";
import { readTestCard, TEST_CARDS } from "./cardReader";

const visa = TEST_CARDS[0]!;

describe("readTestCard", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("hands the API's stub its stand-in when there is no PayMongo key", async () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);
    expect(await readTestCard(visa, null)).toBe("pm_stub_success");
    expect(fetch).not.toHaveBeenCalled();
  });

  it("creates a PayMongo payment method with the public key, and returns only its id", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ data: { id: "pm_abc" } }), { status: 200 }));
    vi.stubGlobal("fetch", fetch);

    expect(await readTestCard(visa, "pk_test_x")).toBe("pm_abc");
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe("https://api.paymongo.com/v1/payment_methods");
    expect(init.headers.Authorization).toBe(`Basic ${btoa("pk_test_x:")}`);
    expect(JSON.parse(init.body).data.attributes.details.card_number).toBe(visa.number);
  });

  it("surfaces PayMongo's reason when the card can't be read", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ errors: [{ detail: "card_number is invalid" }] }), { status: 400 })));
    await expect(readTestCard(visa, "pk_test_x")).rejects.toThrow("card_number is invalid");
  });

  it("only offers PayMongo's published test cards", () => {
    expect(TEST_CARDS.map((c) => c.number)).toEqual([
      "4343434343434345", "5123000000000002", "4120000000000007",
      "4111111111111111", "5100000000000198", "4200000000000018",
    ]);
  });
});
