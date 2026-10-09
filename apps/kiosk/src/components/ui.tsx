import type { ButtonHTMLAttributes, ReactNode } from "react";
import { BRAND } from "../brand";

const peso = new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" });
export const money = (n: number) => peso.format(n);
export const delta = (n: number) => (n === 0 ? "" : `${n > 0 ? "+" : "−"}${peso.format(Math.abs(n))}`);

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  /** primary = the one thing to touch next. choice = a big equal option. quiet = a way back out. */
  variant?: "primary" | "choice" | "quiet" | "plain";
  size?: "md" | "lg" | "xl";
};

/**
 * Every touch target clears 48 px at the smallest size (docs §12), and grows with the viewport so the
 * same component suits a phone and a 1080x1920 panel.
 */
export function Button({ variant = "plain", size = "md", className = "", ...props }: ButtonProps) {
  const sizes = {
    md: "min-h-12 px-4 sm:px-5 text-[length:var(--text-step-0)]",
    lg: "min-h-14 sm:min-h-16 px-5 sm:px-7 text-[length:var(--text-step-1)]",
    xl: "min-h-28 sm:min-h-36 px-6 sm:px-8 text-[length:var(--text-step-3)]",
  };
  const variants = {
    primary: "bg-brand text-paper border-brand-deep shadow-[inset_0_-5px_0_var(--color-brand-deep)]",
    choice: "bg-card text-ink border-line hover:bg-paper-deep",
    quiet: "bg-transparent text-ink-soft border-transparent underline underline-offset-4",
    plain: "bg-card text-ink border-line",
  };
  return (
    <button
      type="button"
      className={`press border-3 font-semibold disabled:opacity-30 disabled:shadow-none ${sizes[size]} ${variants[variant]} ${className}`}
      {...props}
    />
  );
}

/** The red strip across the top. The only place the awning stripe appears. */
export function BrandBar({ right }: { right?: ReactNode }) {
  return (
    <div className="flex items-center gap-3 bg-brand px-4 py-2 text-paper sm:px-6">
      <span className="stripe h-7 w-7 shrink-0 border-2 border-paper" aria-hidden="true" />
      <span className="font-[family-name:var(--font-display)] text-[length:var(--text-step-1)] tracking-tight">
        {BRAND.name}
      </span>
      {right && <div className="ml-auto flex items-center gap-3">{right}</div>}
    </div>
  );
}

/** Full-screen page: brand strip, optional title row, scrolling body, optional action bar. */
export function Screen({
  title,
  onBack,
  children,
  footer,
  badge,
}: {
  title?: string;
  onBack?: () => void;
  children: ReactNode;
  footer?: ReactNode;
  badge?: ReactNode;
}) {
  return (
    <div className="flex h-full flex-col bg-paper">
      <BrandBar right={badge} />
      {(title || onBack) && (
        <header className="flex min-h-16 items-center gap-4 border-b-3 border-line bg-card px-4 py-3 sm:px-6">
          {onBack && (
            <Button variant="plain" onClick={onBack} aria-label="Go back">
              Back
            </Button>
          )}
          {title && (
            <h1 className="font-[family-name:var(--font-display)] text-[length:var(--text-step-2)] leading-none">
              {title}
            </h1>
          )}
        </header>
      )}
      <main className="min-h-0 flex-1 overflow-y-auto">{children}</main>
      {footer && <footer className="border-t-3 border-line bg-card p-3 sm:p-4">{footer}</footer>}
    </div>
  );
}

/** The price block — an amber stamp, the one place yellow is a fill. */
export function Price({ value, className = "" }: { value: number; className?: string }) {
  return (
    <span
      className={`notch-sm inline-block bg-accent px-3 py-1 font-[family-name:var(--font-display)] text-[length:var(--text-step-1)] text-ink ${className}`}
    >
      {money(value)}
    </span>
  );
}

export function Modal({ children }: { children: ReactNode }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink/60 p-5 sm:p-8">
      <div className="notch w-full max-w-xl border-3 border-line bg-card p-6 text-center sm:p-8">{children}</div>
    </div>
  );
}
