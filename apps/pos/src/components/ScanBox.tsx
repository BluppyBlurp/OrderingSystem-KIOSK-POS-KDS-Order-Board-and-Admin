import { ApiError } from "@kiosk/api-client";
import { useEffect, useRef, useState } from "react";
import { lookupOrder, type Order } from "../api";

/**
 * USB/Bluetooth QR scanners behave like keyboards: they type the slip token and press Enter.
 * So this one box takes both a scanned slip and a typed order number, and it grabs focus back after clicks,
 * and whenever `focusKey` changes (a new order opened or finished), so the next scan always lands here.
 */
export function ScanBox({ onFound, focusKey }: { onFound: (order: Order) => void; focusKey?: string }) {
  const input = useRef<HTMLInputElement>(null);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const refocus = () => {
      const active = document.activeElement;
      const typingElsewhere = active instanceof HTMLInputElement || active instanceof HTMLTextAreaElement;
      if (!typingElsewhere && !document.querySelector("[data-modal]")) input.current?.focus();
    };
    refocus();
    window.addEventListener("pointerup", refocus);
    return () => window.removeEventListener("pointerup", refocus);
  }, []);

  useEffect(() => {
    input.current?.focus();
  }, [focusKey]);

  const submit = async () => {
    const value = code.trim();
    if (!value) return;
    setBusy(true);
    setError(null);
    try {
      onFound(await lookupOrder(value));
      setCode("");
    } catch (e) {
      setError(e instanceof ApiError && e.status === 404 ? `No order matches "${value}".` : "Lookup failed. Try again.");
    } finally {
      setBusy(false);
      input.current?.focus();
    }
  };

  return (
    <form
      className="border-b-4 border-black p-4"
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <label htmlFor="scan" className="mb-2 block text-sm font-bold uppercase">
        Scan slip or type order number
      </label>
      <input
        id="scan"
        ref={input}
        value={code}
        onChange={(e) => setCode(e.target.value)}
        disabled={busy}
        autoComplete="off"
        placeholder="e.g. 101"
        className="min-h-14 w-full border-4 border-black px-3 text-2xl font-bold"
      />
      {error && <p className="mt-2 font-bold">{error}</p>}
    </form>
  );
}
