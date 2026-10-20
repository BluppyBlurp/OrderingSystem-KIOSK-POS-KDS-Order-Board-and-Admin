import { useQuery, useQueryClient } from "@tanstack/react-query";
import { QRCodeSVG } from "qrcode.react";
import { useEffect, useState, type ReactNode } from "react";
import { cancelCheckout, getOrder, payOrder, simulatePayment, tapCard, type KioskOrder } from "../api";
import { PrintableReceipt, usePrintOnce } from "../components/Receipt";
import { Button, Modal, money, Screen } from "../components/ui";
import { config } from "../config";
import { useAutoReturn } from "../hooks/useIdleReset";
import { readTestCard, TEST_CARDS, type TestCard } from "../lib/cardReader";
import { useWatchOrder } from "../realtime";
import { useKiosk } from "../store";

const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

function WhereToWait({ order }: { order: KioskOrder["order"] }) {
  return (
    <p className="text-[length:var(--text-step-1)] text-ink-soft">
      {order.type === "ServeToTable"
        ? `We'll bring your food to table ${order.tableNumber}.`
        : "Watch the board — we'll call your number at the counter."}
    </p>
  );
}

/** The order number, set as the largest thing on the screen because it is what the customer needs. */
function OrderNumber({ value }: { value: string }) {
  return (
    <div className="border-3 border-line bg-card px-8 py-3 sm:px-14">
      <span className="display text-[length:var(--text-step-6)] leading-none text-brand">
        {value}
      </span>
    </div>
  );
}

/** Pay at counter: the slip (QR + number) shows on screen and prints. The cashier scans it. */
export function CashSlipScreen() {
  const { placedOrder, reset } = useKiosk();
  const left = useAutoReturn(45, reset);
  usePrintOnce(`${placedOrder?.order.id}:slip`);
  if (!placedOrder) return null;
  const { order, slipToken } = placedOrder;

  return (
    <Screen
      footer={
        <div className="flex items-center gap-3">
          <span className="text-[length:var(--text-step-0)] text-ink-soft">Starting over in {left}s</span>
          <Button variant="primary" size="lg" className="notch-sm ml-auto" onClick={reset}>
            Done
          </Button>
        </div>
      }
    >
      <div className="mx-auto flex max-w-2xl flex-col items-center gap-4 p-5 text-center sm:gap-5 sm:p-8">
        <h2 className="display text-[length:var(--text-step-3)] leading-tight">
          Pay at the counter
        </h2>
        <p className="text-[length:var(--text-step-1)] text-ink-soft">Your order number</p>
        <OrderNumber value={order.orderNumber} />
        <p className="display text-[length:var(--text-step-3)]">{money(order.total)}</p>
        {slipToken && (
          <div className="border-3 border-line bg-card p-3">
            <QRCodeSVG value={slipToken} size={200} />
          </div>
        )}
        <p className="text-[length:var(--text-step-0)] text-ink-soft">
          Take your slip to the cashier before {time(order.expiresAt)}. We start cooking once it's paid.
        </p>
        <WhereToWait order={order} />
      </div>
      <PrintableReceipt kioskOrder={placedOrder} />
    </Screen>
  );
}

/** Footer and error line shared by the QR Ph and card screens. */
function PayFooter({ children }: { children: ReactNode }) {
  return <div className="flex flex-wrap items-center gap-2 sm:gap-3">{children}</div>;
}

function ErrorLine({ error }: { error: string | null }) {
  if (!error) return null;
  return (
    <p className="border-3 border-brand bg-card p-3 text-[length:var(--text-step-0)] font-semibold text-brand">{error}</p>
  );
}

/** A card held over a reader. */
function TapIcon() {
  const line = { stroke: "currentColor", strokeWidth: 3, fill: "none", strokeLinejoin: "round" as const };
  return (
    <svg viewBox="0 0 96 72" className="h-28 w-36 text-ink" aria-hidden="true">
      <rect x="10" y="40" width="76" height="28" rx="4" {...line} />
      <rect x="28" y="6" width="48" height="30" rx="3" {...line} transform="rotate(-12 52 21)" fill="#fff" />
      <path d="M62 50 q6 6 0 12 M70 46 q10 10 0 20" {...line} stroke="#b32317" />
    </svg>
  );
}

/**
 * Stand-in for the reader the kiosk will have. Each button is one tap of a PayMongo test card.
 * Shown only when VITE_CARD_READER=simulated.
 */
function SimulatedReader({ disabled, onTap }: { disabled: boolean; onTap: (card: TestCard) => void }) {
  return (
    <div className="w-full border-3 border-dashed border-line bg-paper p-4 text-left">
      <p className="text-[length:var(--text-step-0)] font-semibold">Simulated card reader · PayMongo test cards</p>
      <div className="mt-3 grid gap-2 sm:grid-cols-2">
        {TEST_CARDS.map((card) => (
          <Button
            key={card.number}
            disabled={disabled}
            onClick={() => onTap(card)}
            className="flex items-center justify-between gap-3 text-left"
          >
            <span>{card.label}</span>
            <span className="shrink-0 whitespace-nowrap tabular-nums text-ink-soft">•••• {card.number.slice(-4)}</span>
          </Button>
        ))}
      </div>
    </div>
  );
}

/**
 * QR Ph and card. QR Ph: the customer scans PayMongo's QR Ph code with any InstaPay bank or e-wallet app.
 * Card: the customer taps the reader; the API settles it at once, or hands over the bank's 3DS page.
 * Either way the API, not the kiosk, decides an order is Paid.
 */
export function OnlinePayScreen() {
  const { placedOrder, setPlacedOrder, reset } = useKiosk();
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const orderId = placedOrder?.order.id;
  useWatchOrder(orderId);

  // Realtime pushes refresh this query at once; polling is the fallback, and each poll also has the API check
  // PayMongo, so a QR Ph payment settles even when no webhook can reach the API.
  const { data: latest } = useQuery({
    queryKey: ["order", orderId],
    queryFn: () => getOrder(orderId!),
    enabled: !!orderId,
    refetchInterval: 2000,
  });

  const status = latest?.order.status ?? placedOrder?.order.status;
  useEffect(() => {
    if (latest && latest.order.status === "Paid") setPlacedOrder(latest, "receipt");
  }, [latest, setPlacedOrder]);

  if (!placedOrder) return null;
  const { order, qrImage, qrTestUrl, cardPublicKey } = placedOrder;
  const checkoutUrl = latest?.checkoutUrl ?? placedOrder.checkoutUrl;
  const method = order.paymentMethod === "Card" ? "Card" : "QrPh";
  const methodLabel = method === "Card" ? "card" : "QR Ph";

  const run = async (action: () => Promise<KioskOrder>, screen: "onlinePay" | "cashSlip" = "onlinePay") => {
    setBusy(true);
    setError(null);
    try {
      const next = await action();
      queryClient.setQueryData(["order", next.order.id], next); // don't flash the old status until the next poll
      setPlacedOrder(next, next.order.status === "Paid" ? "receipt" : screen);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  };

  // From an open payment the API needs the cancel first; a failed payment can switch straight to cash.
  const switchToCash = () =>
    run(async () => {
      if (status === "PaymentPending") await cancelCheckout(order.id);
      return payOrder(order.id, "Cash");
    }, "cashSlip");
  const retry = () => run(() => payOrder(order.id, method));
  const backOut = () => run(() => cancelCheckout(order.id));
  const tap = (card: TestCard) => run(async () => tapCard(order.id, await readTestCard(card, cardPublicKey)));
  const simulate = () => simulatePayment(order.id).catch((e: Error) => setError(e.message));

  if (status === "Expired" || status === "Cancelled") {
    return (
      <Modal>
        <p className="display text-[length:var(--text-step-3)]">This payment timed out</p>
        <p className="mt-3 text-[length:var(--text-step-1)] text-ink-soft">Nothing was charged. Please start a new order.</p>
        <Button variant="primary" size="lg" className="notch-sm mt-7 w-full" onClick={reset}>
          Start over
        </Button>
      </Modal>
    );
  }

  if (status === "Failed") {
    return (
      <Screen>
        <div className="mx-auto flex max-w-3xl flex-col items-center gap-5 p-5 text-center sm:p-10">
          <h2 className="display text-[length:var(--text-step-3)] leading-tight">
            The payment didn't go through
          </h2>
          <p className="text-[length:var(--text-step-1)] text-ink-soft">
            {method === "Card"
              ? "Your card may have been declined. Try again, use another card, or pay at the counter."
              : "Try again, or pay at the counter instead."}
          </p>
          <p className="text-[length:var(--text-step-0)] text-ink-soft">
            Nothing has been charged. Order {order.orderNumber} is held until {time(order.expiresAt)}.
          </p>
          <div className="grid w-full gap-4 sm:grid-cols-2">
            <Button variant="choice" size="xl" disabled={busy} onClick={retry}>
              Try {methodLabel} again
            </Button>
            <Button variant="primary" size="xl" className="notch-sm" disabled={busy} onClick={switchToCash}>
              Pay at the counter
            </Button>
          </div>
          <ErrorLine error={error} />
          <Button variant="quiet" disabled={busy} onClick={reset}>
            Start over
          </Button>
        </div>
      </Screen>
    );
  }

  const footer = (dev: ReactNode) => (
    <PayFooter>
      <Button disabled={busy} onClick={backOut}>
        Cancel
      </Button>
      {config.isDev && dev}
      <Button className="ml-auto" disabled={busy} onClick={switchToCash}>
        Pay at the counter instead
      </Button>
    </PayFooter>
  );

  if (method === "Card") {
    // The bank wants 3D Secure: the customer approves on their own phone, then the webhook or the poll settles it.
    if (checkoutUrl) {
      return (
        <Screen
          footer={footer(
            <Button disabled={busy} onClick={simulate}>
              Simulate approval (dev)
            </Button>,
          )}
        >
          <div className="mx-auto flex max-w-2xl flex-col items-center gap-4 p-5 text-center sm:gap-5 sm:p-8">
            <h2 className="display text-[length:var(--text-step-3)] leading-tight">Your bank wants to confirm it's you</h2>
            <p className="text-[length:var(--text-step-1)] text-ink-soft">
              Scan with your phone and approve the {money(order.total)} payment.
            </p>
            <div className="border-3 border-line bg-card p-4">
              <QRCodeSVG value={checkoutUrl} size={240} />
            </div>
            <p className="animate-pulse display text-[length:var(--text-step-1)]">Waiting for your bank…</p>
            <ErrorLine error={error} />
          </div>
        </Screen>
      );
    }

    return (
      <Screen footer={footer(null)}>
        <div className="mx-auto flex max-w-2xl flex-col items-center gap-4 p-5 text-center sm:gap-5 sm:p-8">
          <h2 className="display text-[length:var(--text-step-3)] leading-tight">Tap your card on the reader</h2>
          <p className="text-[length:var(--text-step-1)] text-ink-soft">{money(order.total)} · Visa or Mastercard</p>
          <TapIcon />
          <p className={`display text-[length:var(--text-step-1)] ${busy ? "animate-pulse" : ""}`}>
            {busy ? "Reading your card…" : "Hold it flat against the reader until it beeps."}
          </p>
          <p className="text-[length:var(--text-step-0)] text-ink-soft">
            Order {order.orderNumber} · pay before {time(order.expiresAt)}
          </p>
          <ErrorLine error={error} />
          {config.cardReader === "simulated" && <SimulatedReader disabled={busy} onTap={tap} />}
        </div>
      </Screen>
    );
  }

  return (
    <Screen
      footer={footer(
        qrTestUrl ? (
          <Button disabled={busy} onClick={() => window.open(qrTestUrl, "_blank", "noopener")}>
            Pay test QR (sandbox)
          </Button>
        ) : (
          <Button disabled={busy} onClick={simulate}>
            Simulate payment (dev)
          </Button>
        ),
      )}
    >
      <div className="mx-auto flex max-w-2xl flex-col items-center gap-4 p-5 text-center sm:gap-5 sm:p-8">
        <h2 className="display text-[length:var(--text-step-3)] leading-tight">Scan to pay with QR Ph</h2>
        <p className="text-[length:var(--text-step-1)] text-ink-soft">
          {money(order.total)} · open GCash, Maya or your bank app and scan
        </p>
        {qrImage ? (
          <div className="border-3 border-line bg-card p-3">
            <img
              src={qrImage}
              alt={`QR Ph code for ${money(order.total)}`}
              className="h-64 w-64 [image-rendering:pixelated]"
            />
          </div>
        ) : checkoutUrl ? (
          <div className="border-3 border-line bg-card p-4">
            <QRCodeSVG value={checkoutUrl} size={260} />
          </div>
        ) : (
          <p className="text-[length:var(--text-step-1)]">Getting the code ready…</p>
        )}
        <p className="text-[length:var(--text-step-0)] text-ink-soft">
          Order {order.orderNumber} · pay before {time(order.expiresAt)}
        </p>
        <p className="animate-pulse display text-[length:var(--text-step-1)]">Waiting for payment…</p>
        <ErrorLine error={error} />
      </div>
    </Screen>
  );
}

/** Paid: thank-you screen and printed receipt. */
export function ReceiptScreen() {
  const { placedOrder, reset } = useKiosk();
  const left = useAutoReturn(30, reset);
  usePrintOnce(`${placedOrder?.order.id}:receipt`);
  if (!placedOrder) return null;
  const { order } = placedOrder;

  return (
    <Screen
      footer={
        <div className="flex items-center gap-3">
          <span className="text-[length:var(--text-step-0)] text-ink-soft">Starting over in {left}s</span>
          <Button variant="primary" size="lg" className="notch-sm ml-auto" onClick={reset}>
            Done
          </Button>
        </div>
      }
    >
      <div className="mx-auto flex max-w-2xl flex-col items-center gap-4 p-5 text-center sm:gap-6 sm:p-10">
        <h2 className="display text-[length:var(--text-step-4)] leading-none text-brand">
          Salamat!
        </h2>
        <p className="text-[length:var(--text-step-1)] text-ink-soft">Paid. Your order number is</p>
        <OrderNumber value={order.orderNumber} />
        <WhereToWait order={order} />
        <p className="text-[length:var(--text-step-0)] text-ink-soft">Please take your receipt.</p>
      </div>
      <PrintableReceipt kioskOrder={placedOrder} />
    </Screen>
  );
}
