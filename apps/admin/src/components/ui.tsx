import type { ButtonHTMLAttributes, ReactNode } from "react";

const peso = new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" });
export const money = (n: number) => peso.format(n);

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  /** primary = the main action on a screen. danger = deletes or revokes. */
  variant?: "primary" | "danger" | "plain";
  size?: "sm" | "md" | "lg";
};

export function Button({ variant = "plain", size = "md", className = "", ...props }: ButtonProps) {
  const sizes = {
    sm: "min-h-8 px-2 text-[length:var(--text-step-00)]",
    md: "min-h-10 px-3 text-[length:var(--text-step-0)]",
    lg: "min-h-12 px-5 text-[length:var(--text-step-1)]",
  };
  const variants = {
    primary: "bg-brand text-paper border-brand-deep",
    danger: "bg-card text-brand border-brand",
    plain: "bg-card text-ink border-line hover:bg-paper-deep",
  };
  return (
    <button
      type="button"
      className={`press border-2 font-semibold disabled:opacity-40 ${sizes[size]} ${variants[variant]} ${className}`}
      {...props}
    />
  );
}

/** A folded paper corner — the house mark. */
export function Mark({ className = "" }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true">
      <path d="M2 2 H22 V16 L14 22 H2 Z" fill="currentColor" />
      <path d="M22 16 H14 V22 Z" fill="currentColor" opacity="0.45" />
    </svg>
  );
}

export function Modal({ children }: { children: ReactNode }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink/60 p-5">
      <div className="max-h-full w-full max-w-lg overflow-y-auto border-2 border-line bg-card p-5">{children}</div>
    </div>
  );
}

/** A page heading. Admin pages are dense, so headings stay small and do their work by weight. */
export function PageTitle({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <h2 className="display text-[length:var(--text-step-2)]">{children}</h2>
      {action}
    </div>
  );
}

/** A bordered white panel — the default container for a table or a form. */
export function Panel({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <div className={`border-2 border-line bg-card ${className}`}>{children}</div>;
}

/** Table shell: one place to change row rules, header weight and number alignment. */
export function Table({ head, children }: { head: ReactNode; children: ReactNode }) {
  return (
    <div className="overflow-x-auto border-2 border-line bg-card">
      <table className="w-full border-collapse text-left text-[length:var(--text-step-0)]">
        <thead className="border-b-2 border-line bg-paper-deep">
          <tr className="[&>th]:px-3 [&>th]:py-2 [&>th]:font-semibold">{head}</tr>
        </thead>
        <tbody className="[&>tr]:border-b [&>tr]:border-paper-deep [&>tr:last-child]:border-b-0 [&>tr>td]:px-3 [&>tr>td]:py-2">
          {children}
        </tbody>
      </table>
    </div>
  );
}

/** A small state word — "Available", "Hidden", "Sold out". */
export function Tag({ tone = "neutral", children }: { tone?: "good" | "warn" | "bad" | "neutral"; children: ReactNode }) {
  const tones = {
    good: "bg-accent text-ink",
    warn: "bg-paper-deep text-ink",
    bad: "bg-brand text-paper",
    neutral: "bg-paper-deep text-ink-soft",
  };
  return (
    <span className={`inline-block px-2 py-0.5 text-[length:var(--text-step-00)] font-semibold ${tones[tone]}`}>
      {children}
    </span>
  );
}

export const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

/** "Dine in · Table 12", "Take out · Counter" … */
export function whereLabel(order: { diningOption: string; type: string; tableNumber: number | null }) {
  const dining = order.diningOption === "TakeOut" ? "Take out" : "Dine in";
  return `${dining} · ${order.type === "ServeToTable" ? `Table ${order.tableNumber}` : "Counter"}`;
}
