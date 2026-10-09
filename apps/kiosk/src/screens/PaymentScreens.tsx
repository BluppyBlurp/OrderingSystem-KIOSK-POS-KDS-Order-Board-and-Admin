import { useQuery, useQueryClient } from "@tanstack/react-query";
import { QRCodeSVG } from "qrcode.react";
import { useEffect, useState } from "react";
import { cancelCheckout, getOrder, payOrder, simulatePayment, type KioskOrder } from "../api";
import { PrintableReceipt, usePrintOnce } from "../components/Receipt";
import { Button, Modal, money, Screen } from "../components/ui";
import { config } from "../config";
import { useAutoReturn } from "../hooks/useIdleReset";
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

/** QR Ph / card: the customer scans with their phone and pays on PayMongo's page; the webhook marks it Paid. */
export function OnlinePayScreen() {
  const { placedOrder, setPlacedOrder, reset } = useKiosk();
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const orderId = placedOrder?.order.id;
  useWatchOrder(orderId);

  // Realtime pushes refresh this query at once; polling is the fallback the docs call for.
  // Either way the webhook is the only thing that sets Paid.
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
  const { order, checkoutUrl } = placedOrder;
  const method = order.paymentMethod === "Card" ? "Card" : "QrPh";
  const methodLabel = method === "Card" ? "card" : "QR Ph";

  const run = async (action: () => Promise<KioskOrder>, screen: "onlinePay" | "cashSlip") => {
    setBusy(true);
    setError(null);
    try {
      const next = await action();
      queryClient.setQueryData(["order", next.order.id], next); // don't flash the old status until the next poll
      setPlacedOrder(next, screen);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  };

  // From an open checkout the API needs the cancel first; a failed payment can switch straight to cash.
  const switchToCash = () =>
    run(async () => {
      if (status === "PaymentPending") await cancelCheckout(order.id);
      return payOrder(order.id, "Cash");
    }, "cashSlip");
  const retry = () => run(() => payOrder(order.id, method), "onlinePay");
  const backOut = () => run(() => cancelCheckout(order.id), "onlinePay");

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
          {error && (
            <p className="border-3 border-brand bg-card p-3 text-[length:var(--text-step-0)] font-semibold text-brand">
              {error}
            </p>
          )}
          <Button variant="quiet" disabled={busy} onClick={reset}>
            Start over
          </Button>
        </div>
      </Screen>
    );
  }

  return (
    <Screen
      footer={
        <div className="flex flex-wrap items-center gap-2 sm:gap-3">
          <Button disabled={busy} onClick={backOut}>
            Cancel
          </Button>
          {config.isDev && (
            <Button disabled={busy} onClick={() => simulatePayment(order.id).catch((e: Error) => setError(e.message))}>
              Simulate payment (dev)
            </Button>
          )}
          <Button className="ml-auto" disabled={busy} onClick={switchToCash}>
            Pay at the counter instead
          </Button>
        </div>
      }
    >
      <div className="mx-auto flex max-w-2xl flex-col items-center gap-4 p-5 text-center sm:gap-5 sm:p-8">
        <h2 className="display text-[length:var(--text-step-3)] leading-tight">
          Scan to pay with your phone
        </h2>
        <p className="text-[length:var(--text-step-1)] text-ink-soft">
          {money(order.total)} by {methodLabel}
        </p>
        {checkoutUrl ? (
          <div className="border-3 border-line bg-card p-4">
            <QRCodeSVG value={checkoutUrl} size={260} />
          </div>
        ) : (
          <p className="text-[length:var(--text-step-1)]">Getting the code ready…</p>
        )}
        <p className="text-[length:var(--text-step-0)] text-ink-soft">
          Order {order.orderNumber} · pay before {time(order.expiresAt)}
        </p>
        <p className="animate-pulse display text-[length:var(--text-step-1)]">
          Waiting for payment…
        </p>
        {error && (
          <p className="border-3 border-brand bg-card p-3 text-[length:var(--text-step-0)] font-semibold text-brand">
            {error}
          </p>
        )}
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
