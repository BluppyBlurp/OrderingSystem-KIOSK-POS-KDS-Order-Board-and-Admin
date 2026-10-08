import { useEffect } from "react";
import { createPortal } from "react-dom";
import type { CashConfirmation } from "../api";
import { money, whereLabel } from "./ui";

const STORE_NAME = "FOOD KIOSK";

/** Official receipt after cash is taken, rendered into #print-root and printed (80 mm thermal layout). */
export function CashReceipt({ result, cashier }: { result: CashConfirmation; cashier: string }) {
  const { order, amountTendered, changeDue } = result;
  const target = document.getElementById("print-root");
  if (!target) return null;

  return createPortal(
    <div style={{ width: "72mm", padding: "4mm", fontFamily: "monospace", fontSize: "12px", color: "#000" }}>
      <div style={{ textAlign: "center", fontWeight: 700, fontSize: "16px" }}>{STORE_NAME}</div>
      <div style={{ textAlign: "center" }}>OFFICIAL RECEIPT</div>
      <hr />
      <div style={{ textAlign: "center", fontSize: "40px", fontWeight: 900 }}>{order.orderNumber}</div>
      <div style={{ textAlign: "center" }}>{whereLabel(order).toUpperCase()}</div>
      <hr />
      {order.items.map((item, i) => (
        <div key={i} style={{ marginBottom: "4px" }}>
          <Row label={`${item.quantity} × ${item.name}`} value={money(item.lineTotal)} />
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
      <Row label="Cash" value={money(amountTendered)} />
      <Row label="Change" value={money(changeDue)} bold />
      <hr />
      <div style={{ textAlign: "center" }}>Cashier: {cashier}</div>
      <div style={{ textAlign: "center" }}>{new Date(order.paidAt ?? order.createdAt).toLocaleString()}</div>
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

/** Prints once per key; recorded only when printing actually happens (StrictMode mounts effects twice). */
export function usePrintOnce(key: string) {
  useEffect(() => {
    if (printed.has(key)) return;
    const timer = setTimeout(() => {
      printed.add(key);
      window.print();
    }, 400);
    return () => clearTimeout(timer);
  }, [key]);
}
