/**
 * The card reader seam. A reader's only job is to turn a tapped card into a PayMongo payment method id (pm_…);
 * the API attaches that to the order's payment intent. Card numbers never go to our API.
 *
 * Today the kiosk has no reader, so a simulated one taps PayMongo's published test cards. A real reader replaces
 * this module: most come with an SDK that reads the card and hands back a token or payment method itself.
 */

export type TestCard = { label: string; number: string; outcome: "approved" | "3ds" | "declined"; stub: string };

/** PayMongo's published test cards (docs: payment-acceptance-testing), with the API stub's stand-ins. */
export const TEST_CARDS: TestCard[] = [
  { label: "Visa", number: "4343434343434345", outcome: "approved", stub: "pm_stub_success" },
  { label: "Mastercard", number: "5123000000000002", outcome: "approved", stub: "pm_stub_success" },
  { label: "Visa · bank asks to confirm", number: "4120000000000007", outcome: "3ds", stub: "pm_stub_3ds" },
  { label: "Declined", number: "4111111111111111", outcome: "declined", stub: "pm_stub_declined" },
  { label: "Insufficient funds", number: "5100000000000198", outcome: "declined", stub: "pm_stub_insufficient_funds" },
  { label: "Expired card", number: "4200000000000018", outcome: "declined", stub: "pm_stub_expired" },
];

const PAYMONGO_API = "https://api.paymongo.com/v1";

/**
 * Simulated tap. With a PayMongo public key (sandbox) it creates a real test payment method, the way a reader's
 * SDK would; without one (the API's offline stub) it returns the stub's stand-in.
 */
export async function readTestCard(card: TestCard, publicKey: string | null | undefined): Promise<string> {
  if (!publicKey) return card.stub;

  const res = await fetch(`${PAYMONGO_API}/payment_methods`, {
    method: "POST",
    headers: { Authorization: `Basic ${btoa(`${publicKey}:`)}`, "Content-Type": "application/json" },
    body: JSON.stringify({
      data: {
        attributes: {
          type: "card",
          details: { card_number: card.number, exp_month: 12, exp_year: new Date().getFullYear() + 3, cvc: "123" },
        },
      },
    }),
  });
  const json = (await res.json().catch(() => null)) as
    | { data?: { id?: string }; errors?: { detail?: string }[] }
    | null;
  if (!res.ok || !json?.data?.id) throw new Error(json?.errors?.[0]?.detail ?? "The card couldn't be read. Try again.");
  return json.data.id;
}
