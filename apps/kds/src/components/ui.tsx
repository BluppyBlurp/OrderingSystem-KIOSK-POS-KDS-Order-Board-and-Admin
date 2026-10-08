import type { ButtonHTMLAttributes, ReactNode } from "react";

const peso = new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" });
export const money = (n: number) => peso.format(n);

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "solid" | "outline"; size?: "md" | "lg" };

export function Button({ variant = "outline", size = "md", className = "", ...props }: ButtonProps) {
  const sizes = { md: "min-h-12 px-4 text-lg", lg: "min-h-16 px-6 text-2xl" };
  const variants = { solid: "bg-black text-white", outline: "bg-white text-black" };
  return (
    <button
      type="button"
      className={`${sizes[size]} ${variants[variant]} border-4 border-black font-bold uppercase active:translate-y-0.5 disabled:opacity-30 disabled:active:translate-y-0 ${className}`}
      {...props}
    />
  );
}

export function Modal({ children }: { children: ReactNode }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-white/80 p-8">
      <div className="w-full max-w-lg border-8 border-black bg-white p-6">{children}</div>
    </div>
  );
}

export const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

/** "Dine in · Table 12", "Take out · Counter" … */
export function whereLabel(order: { diningOption: string; type: string; tableNumber: number | null }) {
  const dining = order.diningOption === "TakeOut" ? "Take out" : "Dine in";
  return `${dining} · ${order.type === "ServeToTable" ? `Table ${order.tableNumber}` : "Counter"}`;
}
