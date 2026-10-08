import { QRCodeSVG } from "qrcode.react";
import { useEffect } from "react";
import { createPortal } from "react-dom";
import type { KioskOrder } from "../api";
import { money } from "./ui";

const STORE_NAME = "FOOD KIOSK";

const paymentLabel: Record<string, string> = { Cash: "Cash", QrPh: "QR Ph", Card: "Card", EWallet: "E-wallet" };

/**
 * The printable slip/receipt, rendered off-screen into #print-root and printed with window.print().
 * In Chrome kiosk mode started with --kiosk-printing, this prints silently to the default (thermal) printer.
 */
export function PrintableReceipt({ kioskOrder }: { kioskOrder: KioskOrder }) {
  const { order, slipToken } = kioskOrder;
  const isSlip = order.status === "AwaitingPayment";
  const target = document.getElementById("print-root");
  if (!target) return null;

  return createPortal(
    <div style={{ width: "72mm", padding: "4mm", fontFamily: "monospace", fontSize: "12px", color: "#000" }}>
      <div style={{ textAlign: "center", fontWeight: 700, fontSize: "16px" }}>{STORE_NAME}</div>
      <div style={{ textAlign: "center" }}>{isSlip ? "PAYMENT SLIP — PAY AT COUNTER" : "OFFICIAL RECEIPT"}</div>
      <hr />
      <div style={{ textAlign: "center", fontSize: "40px", fontWeight: 900 }}>{order.orderNumber}</div>
      <div style={{ textAlign: "center" }}>
        {order.diningOption === "TakeOut" ? "TAKE OUT" : "DINE IN"} ·{" "}
        {order.type === "ServeToTable" ? `TABLE ${order.tableNumber}` : "PICK UP AT COUNTER"}
      </div>
      <hr />
      {order.items.map((item, i) => (
        <div key={i} style={{ marginBottom: "4px" }}>
          <div style={{ display: "flex", justifyContent: "space-between" }}>
            <span>
              {item.quantity} × {item.name}
            </span>
            <span>{money(item.lineTotal)}</span>
          </div>
          {item.modifiers.map((m, j) => (
            <div key={j} style={{ paddingLeft: "12px" }}>
              - {m.name}
              {m.priceDelta ? ` (${money(m.priceDelta)})` : ""}
            </div>
          ))}
        </div>
      ))}
      <hr />
      <Row label="TOTAL" value={money(order.total)} bold />
      <Row label="VAT (incl.)" value={money(order.taxAmount)} />
      {!isSlip && order.paymentMethod && <Row label="Paid by" value={paymentLabel[order.paymentMethod] ?? order.paymentMethod} />}
      <hr />
      {isSlip && slipToken && (
        <div style={{ textAlign: "center" }}>
          <QRCodeSVG value={slipToken} size={160} />
          <div>Show this at the counter.</div>
          <div>Valid until {new Date(order.expiresAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</div>
        </div>
      )}
      <div style={{ textAlign: "center", marginTop: "6px" }}>{new Date(order.paidAt ?? order.createdAt).toLocaleString()}</div>
      <div style={{ textAlign: "center" }}>Thank you!</div>
    </div>,
    target,
  );
}

function Row({ label, value, bold }: { label: string; value: string; bold?: boolean }) {
  return (
    <div style={{ display: "flex", justifyContent: "space-between", fontWeight: bold ? 700 : 400 }}>
      <span>{label}</span>
      <span>{value}</span>
    </div>
  );
}

const printed = new Set<string>();

/**
 * Prints once per order+status, even if React mounts the screen twice (StrictMode) or re-renders.
 * The key is recorded only when printing actually happens: StrictMode's simulated unmount cancels the
 * first timer, and the second mount must still print.
 */
export function usePrintOnce(key: string) {
  useEffect(() => {
    if (printed.has(key)) return;
    const timer = setTimeout(() => {
      printed.add(key);
      window.print();
    }, 400); // let the portal render first
    return () => clearTimeout(timer);
  }, [key]);
}
