import { useState } from "react";
import { createPortal } from "react-dom";
import { useShiftSummary, type ShiftSummary } from "../api";
import { readShiftStart, saveShiftStart } from "../lib/shift";
import { Button, money, time } from "./ui";

/**
 * What the cashier counts the drawer against: the cash they took since their shift started, the whole counter's
 * total, and the orders they voided. "Start new shift" resets the start on this till.
 */
export function ShiftSummaryPanel({ cashier, onClose }: { cashier: string; onClose: () => void }) {
  const [since, setSince] = useState(() => readShiftStart(cashier));
  const [confirmNew, setConfirmNew] = useState(false);
  const [printing, setPrinting] = useState(false);
  const summary = useShiftSummary(since, true);
  const data = summary.data;

  const startNewShift = () => {
    setSince(saveShiftStart(cashier, new Date()));
    setConfirmNew(false);
  };

  const print = () => {
    setPrinting(true);
    // let the slip render into #print-root before the print dialog snapshots the page
    setTimeout(() => {
      window.print();
      setPrinting(false);
    }, 100);
  };

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-ink/60 p-4 sm:p-8" role="dialog" aria-label="Shift summary">
      <div className="flex max-h-full w-full max-w-2xl flex-col gap-5 overflow-y-auto border-3 border-line bg-card p-5 sm:p-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 className="display text-[length:var(--text-step-3)]">Shift summary</h2>
            <p className="text-[length:var(--text-step-0)] text-ink-soft">
              {cashier} ·{" "}
              {data ? `${since ? "since" : "today, since"} ${time(data.from)} · as of ${time(data.to)}` : "loading…"}
            </p>
          </div>
          <Button onClick={onClose} aria-label="Close">
            Close
          </Button>
        </div>

        {summary.isError && (
          <p className="border-3 border-brand bg-card p-3 text-[length:var(--text-step-0)] font-semibold text-brand">
            Couldn't load the summary. {summary.error instanceof Error ? summary.error.message : ""}
          </p>
        )}

        {data && (
          <>
            <div className="border-3 border-line bg-accent p-5">
              <p className="text-[length:var(--text-step-0)] font-semibold">Your cash in the drawer</p>
              <p className="display text-[length:var(--text-step-5)] leading-none tabular-nums">{money(data.mine.cashCollected)}</p>
              <p className="mt-1 text-[length:var(--text-step-0)]">
                {data.mine.orders} cash {data.mine.orders === 1 ? "order" : "orders"}
              </p>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <Stat label="Whole counter" value={money(data.allCashiers.cashCollected)} note={`${data.allCashiers.orders} ${data.allCashiers.orders === 1 ? "order" : "orders"}, all cashiers, same period`} />
              <Stat label="Voided by you" value={String(data.cancelledByMe)} note="unpaid orders cancelled" />
            </div>
            <p className="text-[length:var(--text-step-0)] text-ink-soft">
              Counts order totals paid in cash, not the change given back. Card and QR Ph payments never reach the drawer.
            </p>
          </>
        )}

        <div className="flex flex-wrap items-center justify-between gap-3">
          {confirmNew ? (
            <div className="flex items-center gap-3">
              <span className="text-[length:var(--text-step-0)] font-semibold">Start counting from now?</span>
              <Button variant="primary" onClick={startNewShift}>
                Yes, new shift
              </Button>
              <Button onClick={() => setConfirmNew(false)}>No</Button>
            </div>
          ) : (
            <Button onClick={() => setConfirmNew(true)}>Start new shift</Button>
          )}
          <div className="flex gap-3">
            <Button disabled={summary.isFetching} onClick={() => void summary.refetch()}>
              {summary.isFetching ? "Refreshing…" : "Refresh"}
            </Button>
            <Button variant="primary" disabled={!data} onClick={print}>
              Print
            </Button>
          </div>
        </div>
      </div>
      {printing && data && <ShiftSlip summary={data} cashier={cashier} />}
    </div>
  );
}

function Stat({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <div className="border-3 border-line bg-paper p-4">
      <p className="text-[length:var(--text-step-0)] font-semibold text-ink-soft">{label}</p>
      <p className="display text-[length:var(--text-step-3)] leading-tight tabular-nums">{value}</p>
      <p className="text-[length:var(--text-step-0)] text-ink-soft">{note}</p>
    </div>
  );
}

/** 80 mm printout for the drawer count, same thermal layout as the receipt. */
function ShiftSlip({ summary, cashier }: { summary: ShiftSummary; cashier: string }) {
  const target = document.getElementById("print-root");
  if (!target) return null;
  const when = (iso: string) => new Date(iso).toLocaleString();

  return createPortal(
    <div style={{ width: "72mm", padding: "4mm", fontFamily: "monospace", fontSize: "12px", color: "#000" }}>
      <div style={{ textAlign: "center", fontWeight: 700, fontSize: "16px" }}>SHIFT SUMMARY</div>
      <div style={{ textAlign: "center" }}>{cashier}</div>
      <hr />
      <Row label="From" value={when(summary.from)} />
      <Row label="To" value={when(summary.to)} />
      <hr />
      <Row label="Your cash orders" value={String(summary.mine.orders)} />
      <Row label="Your cash" value={money(summary.mine.cashCollected)} bold />
      <Row label="Voided by you" value={String(summary.cancelledByMe)} />
      <hr />
      <Row label="Counter orders" value={String(summary.allCashiers.orders)} />
      <Row label="Counter cash" value={money(summary.allCashiers.cashCollected)} />
      <hr />
      <div style={{ marginTop: "16px" }}>Counted: ______________</div>
      <div style={{ marginTop: "16px" }}>Signature: ____________</div>
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
