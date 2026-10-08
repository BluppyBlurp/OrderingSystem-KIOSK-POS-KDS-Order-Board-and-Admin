import type { InputHTMLAttributes, ReactNode } from "react";

export const inputClass = "w-full border-4 border-black px-3 py-2 text-lg";

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-sm font-bold uppercase">{label}</span>
      {children}
    </label>
  );
}

export function TextInput(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={inputClass} {...props} />;
}

export function Check({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="flex min-h-12 items-center gap-3 text-lg font-bold">
      <input type="checkbox" className="size-6 accent-black" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      {label}
    </label>
  );
}

/** Errors stay until dismissed so nothing flashes past unread. */
export function ErrorBox({ message, onDismiss }: { message: string | null; onDismiss: () => void }) {
  if (!message) return null;
  return (
    <button type="button" onClick={onDismiss} className="w-full border-4 border-black bg-black p-3 text-left font-bold text-white">
      {message} (tap to dismiss)
    </button>
  );
}

/** "12.50" → 12.5; blank or invalid → null. */
export function parseNumber(text: string): number | null {
  if (text.trim() === "") return null;
  const n = Number(text);
  return Number.isFinite(n) ? n : null;
}
