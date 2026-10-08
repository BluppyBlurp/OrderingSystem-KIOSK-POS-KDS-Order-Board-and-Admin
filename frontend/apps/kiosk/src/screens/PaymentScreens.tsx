import { useQuery } from "@tanstack/react-query";
import { QRCodeSVG } from "qrcode.react";
import { useEffect, useState } from "react";
import { cancelCheckout, getOrder, payOrder, simulatePayment, type KioskOrder } from "../api";
import { PrintableReceipt, usePrintOnce } from "../components/Receipt";
import { Button, Modal, money, Screen } from "../components/ui";
import { config } from "../config";
import { useAutoReturn } from "../hooks/useIdleReset";
import { useKiosk } from "../store";

const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

function WhereToWait({ order }: { order: KioskOrder["order"] }) {
  return (
    <p className="text-2xl">
      {order.type === "ServeToTable"
        ? `We'll bring your food to table ${order.tableNumber}.`
        : "Watch the screen — we'll call your number at the counter."}
    </p>
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
        <div className="flex items-center justify-between">
          <span className="text-lg">Returning to start in {left}s</span>
          <Button variant="solid" size="lg" onClick={reset}>
            Done
          </Button>
        </div>
      }
    >
      <div className="flex flex-col items-center gap-6 p-8 text-center">
        <p className="text-4xl font-black uppercase">Please pay at the counter</p>
        <p className="text-2xl">Your order number</p>
        <p className="border-8 border-black px-10 text-9xl font-black">{order.orderNumber}</p>
        <p className="text-4xl font-black">{money(order.total)}</p>
        {slipToken && <QRCodeSVG value={slipToken} size={220} />}
        <p className="text-xl">
          Take your printed slip to the cashier before {time(order.expiresAt)}. Your order goes to the kitchen once paid.
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
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const orderId = placedOrder?.order.id;

  // Polling is the fallback the docs call for; the webhook is still the only thing that sets Paid.
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
  const methodLabel = order.paymentMethod === "Card" ? "card" : "QR Ph (GCash, Maya or your bank app)";

  const switchToCash = async () => {
    setBusy(true);
    setError(null);
    try {
      await cancelCheckout(order.id);
      setPlacedOrder(await payOrder(order.id, "Cash"), "cashSlip");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  };

  if (status === "Expired" || status === "Cancelled") {
    return (
      <Modal>
        <p className="text-3xl font-black">This payment timed out.</p>
        <p className="mt-4 text-xl">Nothing was charged. Please start a new order.</p>
        <Button variant="solid" size="lg" className="mt-8 w-full" onClick={reset}>
          Start over
        </Button>
      </Modal>
    );
  }

  return (
    <Screen
      footer={
        <div className="flex items-center justify-between gap-4">
          <Button size="lg" disabled={busy} onClick={switchToCash}>
            Pay at counter instead
          </Button>
          {config.isDev && (
            <Button size="lg" disabled={busy} onClick={() => simulatePayment(order.id).catch((e: Error) => setError(e.message))}>
              Simulate payment (dev)
            </Button>
          )}
        </div>
      }
    >
      <div className="flex flex-col items-center gap-6 p-8 text-center">
        <p className="text-4xl font-black uppercase">Scan to pay with your phone</p>
        <p className="text-2xl">Pay {money(order.total)} by {methodLabel}</p>
        {checkoutUrl ? (
          <div className="border-8 border-black p-4">
            <QRCodeSVG value={checkoutUrl} size={300} />
          </div>
        ) : (
          <p className="text-xl">Preparing payment…</p>
        )}
        <p className="text-xl">Order {order.orderNumber} · pay before {time(order.expiresAt)}</p>
        <p className="animate-pulse text-2xl font-bold">Waiting for payment…</p>
        {error && <p className="border-4 border-black p-3 text-xl font-bold">{error}</p>}
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
        <div className="flex items-center justify-between">
          <span className="text-lg">Returning to start in {left}s</span>
          <Button variant="solid" size="lg" onClick={reset}>
            Done
          </Button>
        </div>
      }
    >
      <div className="flex flex-col items-center gap-6 p-10 text-center">
        <p className="text-5xl font-black uppercase">Thank you!</p>
        <p className="text-2xl">Payment received · your order number</p>
        <p className="border-8 border-black px-10 text-9xl font-black">{order.orderNumber}</p>
        <WhereToWait order={order} />
        <p className="text-xl">Please take your receipt.</p>
      </div>
      <PrintableReceipt kioskOrder={placedOrder} />
    </Screen>
  );
}
