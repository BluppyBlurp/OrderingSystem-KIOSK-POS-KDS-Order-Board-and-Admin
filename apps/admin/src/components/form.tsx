import type { InputHTMLAttributes, ReactNode } from "react";

/** Amber focus, not red: red is reserved for something being wrong. */
export const inputClass =
  "w-full border-2 border-line bg-card px-3 py-2 text-[length:var(--text-step-0)] outline-none focus:border-accent-deep focus:bg-accent/10";

export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-[length:var(--text-step-00)] font-semibold text-ink-soft">{label}</span>
      {children}
      {hint && <span className="text-[length:var(--text-step-00)] text-ink-soft">{hint}</span>}
    </label>
  );
}

export function TextInput(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={inputClass} {...props} />;
}

export function Check({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="flex min-h-10 items-center gap-2.5 text-[length:var(--text-step-0)] font-semibold">
      <input
        type="checkbox"
        className="size-5 accent-[var(--color-brand)]"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      {label}
    </label>
  );
}

/** Errors stay until dismissed so nothing flashes past unread. */
export function ErrorBox({ message, onDismiss }: { message: string | null; onDismiss: () => void }) {
  if (!message) return null;
  return (
    <button
      type="button"
      onClick={onDismiss}
      className="w-full border-2 border-brand bg-card p-3 text-left text-[length:var(--text-step-0)] font-semibold text-brand"
    >
      {message} — click to dismiss
    </button>
  );
}

/** "12.50" → 12.5; blank or invalid → null. */
export function parseNumber(text: string): number | null {
  if (text.trim() === "") return null;
  const n = Number(text);
  return Number.isFinite(n) ? n : null;
}
