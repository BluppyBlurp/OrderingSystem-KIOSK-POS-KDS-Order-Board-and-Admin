import type { ButtonHTMLAttributes, ReactNode } from "react";

const peso = new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" });
export const money = (n: number) => peso.format(n);
export const delta = (n: number) => (n === 0 ? "" : `${n > 0 ? "+" : "−"}${peso.format(Math.abs(n))}`);

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "solid" | "outline" | "ghost";
  size?: "md" | "lg" | "xl";
};

/** Every touch target is at least 48 px (docs §12). */
export function Button({ variant = "outline", size = "md", className = "", ...props }: ButtonProps) {
  const sizes = { md: "min-h-12 px-5 text-lg", lg: "min-h-16 px-6 text-2xl", xl: "min-h-40 px-8 text-4xl" };
  const variants = {
    solid: "bg-black text-white border-4 border-black",
    outline: "bg-white text-black border-4 border-black",
    ghost: "bg-white text-black border-4 border-transparent underline",
  };
  return (
    <button
      type="button"
      className={`${sizes[size]} ${variants[variant]} font-bold uppercase tracking-wide active:translate-y-0.5 disabled:opacity-30 disabled:active:translate-y-0 ${className}`}
      {...props}
    />
  );
}

/** Full-screen page with a title bar and an optional bottom bar. */
export function Screen({ title, onBack, children, footer }: { title?: string; onBack?: () => void; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="flex h-full flex-col">
      {(title || onBack) && (
        <header className="flex min-h-20 items-center gap-4 border-b-4 border-black px-6">
          {onBack && (
            <Button variant="outline" onClick={onBack} aria-label="Back">
              ← Back
            </Button>
          )}
          {title && <h1 className="text-3xl font-black uppercase">{title}</h1>}
        </header>
      )}
      <main className="min-h-0 flex-1 overflow-y-auto">{children}</main>
      {footer && <footer className="border-t-4 border-black p-4">{footer}</footer>}
    </div>
  );
}

/** Placeholder art: one simple black-and-white shape per category until real photos exist. */
export function Shape({ kind, className = "" }: { kind: string; className?: string }) {
  const k = kind.toLowerCase();
  const common = `border-4 border-black ${className}`;
  if (k.includes("drink")) return <div className={`${common} aspect-[2/3] w-1/3 rounded-b-3xl`} />; // cup
  if (k.includes("dessert")) return <div className={`${common} aspect-square w-1/2 rounded-t-full`} />; // dome
  if (k.includes("side")) return <div className={`${common} aspect-square w-1/2 rotate-45`} />; // diamond
  if (k.includes("pasta")) return <div className={`${common} aspect-[2/1] w-2/3 rounded-b-full`} />; // bowl
  if (k.includes("sandwich")) return <div className={`${common} aspect-[2/1] w-2/3 rounded-t-full`} />; // bun
  return <div className={`${common} aspect-square w-1/2 rounded-full`} />; // plate
}

export function Modal({ children }: { children: ReactNode }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-white/80 p-8">
      <div className="w-full max-w-xl border-8 border-black bg-white p-8 text-center">{children}</div>
    </div>
  );
}
