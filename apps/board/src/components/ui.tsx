import type { ButtonHTMLAttributes, ReactNode } from "react";

const peso = new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" });
export const money = (n: number) => peso.format(n);

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "plain"; size?: "md" | "lg" };

export function Button({ variant = "plain", size = "md", className = "", ...props }: ButtonProps) {
  const sizes = { md: "min-h-12 px-4 text-[length:var(--text-step-0)]", lg: "min-h-16 px-6 text-[length:var(--text-step-1)]" };
  const variants = {
    primary: "bg-brand text-paper border-brand-deep shadow-[inset_0_-5px_0_var(--color-brand-deep)]",
    plain: "bg-card text-ink border-line",
  };
  return (
    <button
      type="button"
      className={`press border-3 font-semibold disabled:opacity-40 disabled:shadow-none ${sizes[size]} ${variants[variant]} ${className}`}
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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink/60 p-8">
      <div className="w-full max-w-lg border-3 border-line bg-card p-6">{children}</div>
    </div>
  );
}

export const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

/** "Dine in · Table 12", "Take out · Counter" … */
export function whereLabel(order: { diningOption: string; type: string; tableNumber: number | null }) {
  const dining = order.diningOption === "TakeOut" ? "Take out" : "Dine in";
  return `${dining} · ${order.type === "ServeToTable" ? `Table ${order.tableNumber}` : "Counter"}`;
}
