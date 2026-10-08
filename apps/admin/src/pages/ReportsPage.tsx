import { useState } from "react";
import { errorText, useRefundsNeeded, useSales } from "../api";
import { Field, TextInput } from "../components/form";
import { Button, money } from "../components/ui";

const isoDate = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

/** Sales by business date (both ends inclusive) and the payments that need a manual refund. */
export function ReportsPage() {
  const today = isoDate(new Date());
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const sales = useSales(from, to);
  const refunds = useRefundsNeeded();
  const r = sales.data;

  const preset = (days: number) => {
    const start = new Date();
    start.setDate(start.getDate() - (days - 1));
    setFrom(isoDate(start));
    setTo(today);
  };

  return (
    <div className="flex max-w-6xl flex-col gap-6 p-4">
      <div className="flex flex-wrap items-end gap-3">
        <Field label="From">
          <TextInput type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </Field>
        <Field label="To">
          <TextInput type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </Field>
        <Button onClick={() => preset(1)}>Today</Button>
        <Button onClick={() => preset(7)}>Last 7 days</Button>
        <Button onClick={() => preset(30)}>Last 30 days</Button>
        <Button onClick={() => void sales.refetch()}>Refresh</Button>
      </div>

      {sales.isError && <p className="border-4 border-black p-3 font-bold">{errorText(sales.error)}</p>}
      {sales.isLoading && <p>Loading…</p>}

      {r && (
        <>
          <div className="grid grid-cols-4 gap-4">
            <Stat label="Gross sales" value={money(r.grossSales)} />
            <Stat label="Orders" value={String(r.orders)} />
            <Stat label="Average order" value={money(r.averageOrder)} />
            <Stat label="VAT included" value={money(r.vatAmount)} note={`Net of VAT ${money(r.netOfVat)}`} />
          </div>

          <div className="grid grid-cols-2 gap-6">
            <Table
              title="By payment method"
              head={["Method", "Orders", "Sales"]}
              rows={r.byPaymentMethod.map((m) => [m.method ?? "Unknown", String(m.orders), money(m.sales)])}
            />
            <Table title="By day" head={["Date", "Orders", "Sales"]} rows={r.byDay.map((d) => [d.date, String(d.orders), money(d.sales)])} />
          </div>
          <Table
            title="Top products"
            head={["Product", "Quantity", "Sales"]}
            rows={r.topProducts.map((p) => [p.name, String(p.quantity), money(p.sales)])}
          />
          {r.refundsNeeded > 0 && <p className="text-lg font-bold">{r.refundsNeeded} payment(s) in this period need a refund (see below).</p>}
        </>
      )}

      <section>
        <h2 className="mb-2 text-2xl font-black uppercase">Refunds to make</h2>
        <p className="mb-2">
          Payments that arrived after their order expired or was cancelled, or for the wrong amount. Refund them in the PayMongo dashboard.
        </p>
        {refunds.isError && <p className="font-bold">{errorText(refunds.error)}</p>}
        <Table
          title=""
          head={["When", "Order", "Order status", "Order total", "Details"]}
          rows={(refunds.data ?? []).map((x) => [new Date(x.at).toLocaleString(), x.orderNumber, x.orderStatus, money(x.orderTotal), x.note ?? ""])}
          empty="None. Every payment matched a live order."
        />
      </section>
    </div>
  );
}

function Stat({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="border-4 border-black p-4">
      <p className="text-sm font-bold uppercase">{label}</p>
      <p className="text-3xl font-black tabular-nums">{value}</p>
      {note && <p className="text-sm">{note}</p>}
    </div>
  );
}

function Table({ title, head, rows, empty = "No sales in this period." }: { title: string; head: string[]; rows: string[][]; empty?: string }) {
  return (
    <div>
      {title && <h3 className="mb-2 text-xl font-black uppercase">{title}</h3>}
      <table className="w-full text-left text-lg">
        <thead>
          <tr className="border-b-4 border-black">
            {head.map((h) => (
              <th key={h} className="p-2">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i} className="border-b-2 border-black">
              {row.map((cell, j) => (
                <td key={j} className="p-2">
                  {cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {rows.length === 0 && <p className="p-2">{empty}</p>}
    </div>
  );
}
